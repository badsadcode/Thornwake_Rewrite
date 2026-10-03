using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Thornwake.Core;

public enum ReceiverBoundaryKind { InternalContinuation, UnknownBoundary, CertifiedExternalReceiver }
// A certificate covers the ENTIRE closed target patch, including every entering face. Only the
// frozen production adapter can mint one; point outlets alone cannot establish this contract.
internal sealed record ReceiverPatchCertificate(int Format,int Algorithm,string Domain,string Candidate,int Patch,
    string ReceiverId,double Level,string Evidence,bool Terminal=true);
public sealed record ReceiverPatchObservation(int Patch,ReceiverBoundaryKind Kind,string Reason,string? ReceiverId,
    double? Level,HydrologyCell? FirstCell,MacroPoint? FirstTransition,int ComponentTransitions,int SpillTransitions);
public sealed record ReceiverDomainProfile(int CertificateLimit,int MetadataEntries,int RejectedCertificates,int MetadataRefusals,
    IReadOnlyList<ReceiverPatchObservation> Boundaries,bool? FinalCandidateResolved,SpillCellEvidence? FinalCandidateUnresolvedCell);
public sealed record ProfiledReceiverHydrology(HydrologyTopologyResult Result,HydrologyGraphProfile Graph,
    HydrologySpillAttribution Attribution,ReceiverDomainProfile Receivers);

internal static class OceanPatchProof
{
    // Proof v1 is tied to macro terrain 3/4. Noise is in [-1,1], Smooth in [0,1].
    // Warping moves each coordinate by <= Rx*r/2; coast <= r; lobes <= 1.95*r.
    // A conservative rotated-ellipse interval lower bound on radius therefore proves influence<0.
    // One metre padding and a dimensionless margin intentionally make near-shore proofs fail closed.
    internal static bool Covers(MacroWorldGenerator world,double x,double z,double span)
    {
        if(world.Manifest.Algorithm is not (3 or 4) || !double.IsFinite(x) || !double.IsFinite(z) || !double.IsFinite(span) || span<=0)return false;
        double r=world.Manifest.Profile.CoastRoughness;
        foreach(var f in world.LandFeatures)
        {
            double c=Math.Cos(f.Angle),s=Math.Sin(f.Angle),extent=span*.5+1;
            double dx=x+span*.5-f.Center.X,dz=z+span*.5-f.Center.Z;
            double uncertainty=(extent+f.RadiusX*r*.5)*(Math.Abs(c)+Math.Abs(s));
            double a=Math.Max(0,Math.Abs(c*dx+s*dz)-uncertainty)/f.RadiusX;
            double b=Math.Max(0,Math.Abs(-s*dx+c*dz)-uncertainty)/f.RadiusZ;
            if(Math.Sqrt(a*a+b*b)<=1+2.95*r+1e-8)return false;
        }
        return true;
    }
}

internal sealed partial class AdaptiveBasinTopology
{
    private const int ReceiverCertificateLimit=256;
    private sealed class ReceiverEntry
    {
        internal ReceiverPatchCertificate? Certificate;internal ReceiverBoundaryKind Kind;internal string Reason="";
        internal HydrologyCell? First;internal MacroPoint? Position;internal int Wet,Spill;
    }
    private readonly Dictionary<int,ReceiverEntry> _receiverPatches=new();
    private int _receiverRejections,_receiverRefusals;
    private ReceiverEntry Receiver(int patch)
    {
        _cancel.ThrowIfCancellationRequested();
        if(_receiverPatches.TryGetValue(patch,out var old))return old;
        if(_receiverPatches.Count>=ReceiverCertificateLimit){_receiverRefusals++;return new(){Kind=ReceiverBoundaryKind.UnknownBoundary,Reason="receiver-budget"};}
        var entry=new ReceiverEntry {Kind=ReceiverBoundaryKind.InternalContinuation,Reason="no-area-certificate"};
        var certificate=patch==_seedPatch?null:_d.ReceiverCertificate?.Invoke(patch);
        if(certificate!=null)
        {
            // Recheck the original declared receiver and all query dependencies. No declaration,
            // returning route, point-only evidence or mismatched version can certify escape.
            bool valid=certificate.Format==1 && certificate.Algorithm==5 && certificate.Domain==_d.Identity &&
                certificate.Candidate==_d.CandidateId && certificate.Patch==patch && certificate.Terminal &&
                certificate.Evidence=="whole-patch-ocean-v1" && double.IsFinite(certificate.Level) &&
                _d.Exits(patch).Any(e=>e.Id==certificate.ReceiverId && e.Level==certificate.Level);
            if(valid){entry.Certificate=certificate;entry.Kind=ReceiverBoundaryKind.CertifiedExternalReceiver;entry.Reason=certificate.Evidence;}
            else{entry.Kind=ReceiverBoundaryKind.UnknownBoundary;entry.Reason="receiver-invalid";_receiverRejections++;}
        }
        _receiverPatches.Add(patch,entry);return entry;
    }
    // Every entering point lies in the convex, proved-submerged target rectangle and can reach
    // its existing declared ocean centre below receiver level. The source face midpoint is sampled.
    private bool ReceiverBoundary(Cell c,int patch,long x,long z,out ReconciliationExit? exit,out string? failure,bool observe)
    {
        exit=null;failure=null;if(_r.Algorithm!=5 || Owner(c.Key)==patch)return false;
        var e=Receiver(patch);
        if(e.Kind==ReceiverBoundaryKind.UnknownBoundary){failure=e.Reason;return true;}
        if(e.Certificate is not {} cert)return false;
        if(_samples[(x,z)]>cert.Level){_receiverRejections++;failure="receiver-invalid";return true;}
        var position=Position(x,z);
        if(observe){e.First??=c.Key;e.Position??=position;if(_spillPhase)e.Spill++;else e.Wet++;}
        int quad=(int)(c.Key.Z%_patchUnits/_unit)*_d.Subdivisions+(int)(c.Key.X%_patchUnits/_unit);
        exit=new(cert.ReceiverId,new(Owner(c.Key),quad),position,cert.Level);return true;
    }
    private IEnumerable<ReconciliationExit> BoundaryExits(Cell c)
    {
        if(_r.Algorithm!=5)yield break;
        var k=c.Key;
        foreach(var dir in Directions)
        {
            long nx=dir.X<0?k.X-1:dir.X>0?k.X+k.Size:k.X;
            long nz=dir.Z<0?k.Z-1:dir.Z>0?k.Z+k.Size:k.Z;
            if(nx<0 || nz<0 || nx>=(long)_d.Width*_patchUnits || nz>=(long)_d.Depth*_patchUnits)continue;
            int patch=(int)(nz/_patchUnits)*_d.Width+(int)(nx/_patchUnits);
            long x=dir.X<0?k.X:dir.X>0?k.X+k.Size:k.X+k.Size/2;
            long z=dir.Z<0?k.Z:dir.Z>0?k.Z+k.Size:k.Z+k.Size/2;
            if(ReceiverBoundary(c,patch,x,z,out var exit,out var failure,true))
            {if(failure!=null)Unknown(c,patch,failure,-100000);else yield return exit!;}
        }
    }
    private ReceiverDomainProfile ReceiverProfile(HydrologyTopologyResult result)
    {
        bool? resolved=result.SpillProposal==null?null:true;SpillCellEvidence? first=null;
        if(result.SpillProposal is {} spill)foreach(var key in spill.Cells)
        {
            _cancel.ThrowIfCancellationRequested();var leaf=SpillLookup(key);
            if(leaf==null || leaf.Unresolved){resolved=false;first=leaf;break;}
        }
        return new(ReceiverCertificateLimit,_receiverPatches.Count,_receiverRejections,_receiverRefusals,
        _receiverPatches.OrderBy(p=>p.Key).Select(p=>new ReceiverPatchObservation(p.Key,p.Value.Kind,p.Value.Reason,
            p.Value.Certificate?.ReceiverId,p.Value.Certificate?.Level,p.Value.First,p.Value.Position,p.Value.Wet,p.Value.Spill)).ToArray(),resolved,first);
    }
    internal static ProfiledReceiverHydrology ProfileReceivers(ReconciliationDomain d,HydrologyTopologyRecipe r,int patch,int? quad,CancellationToken cancellation=default)
    {
        if(r.Algorithm!=5)throw new ArgumentException("Receiver-domain prototype requires explicit topology algorithm 5.");
        var query=new AdaptiveBasinTopology(d,r,cancellation);query._graph=new(query._step,cancellation,null);
        query._spillDiagnostics=new(query._step,d.Stage,cancellation);
        var result=query.Execute(patch,quad);return new(result,query._graph.Finish(),query._spillDiagnostics.Finish(),query.ReceiverProfile(result));
    }
}
