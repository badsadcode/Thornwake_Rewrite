using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Thornwake.Core;

public sealed record MacroWorldProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required double WorldSize { get; init; }
    public required int Continents { get; init; }
    public required int Islands { get; init; }
    public required double ContinentRadius { get; init; }
    public required double IslandRadius { get; init; }
    public required double CoastRoughness { get; init; }
    public required double SeaLevel { get; init; }
    public required double OceanDepth { get; init; }
    public required double LowlandHeight { get; init; }
    public required double HillHeight { get; init; }
    public required double HillScale { get; init; }
    public required int RangesPerContinent { get; init; }
    public required double MountainHeight { get; init; }
    public required double MountainWidth { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length>80) throw new InvalidDataException("Invalid world profile name.");
        try { _=new GenerationIdentity(0,3,Id).FeatureId("profile",new(0,0),0); }
        catch(ArgumentException error) { throw new InvalidDataException("Invalid profile identity.",error); }
        bool Range(double x,double min,double max)=>double.IsFinite(x) && x>=min && x<=max;
        if (!Range(WorldSize,16000,4096000) || Continents<1 || Continents>8 || Islands<0 || Islands>128 ||
            !Range(ContinentRadius,WorldSize*.035,WorldSize*.28) || !Range(IslandRadius,WorldSize*.002,WorldSize*.025) ||
            !Range(CoastRoughness,0,.3) || !Range(SeaLevel,-1000,1000) || !Range(OceanDepth,50,8000) ||
            !Range(LowlandHeight,10,800) || !Range(HillHeight,0,800) || !Range(HillScale,500,WorldSize*.1) ||
            RangesPerContinent<0 || RangesPerContinent>6 || !Range(MountainHeight,0,6000) ||
            !Range(MountainWidth,500,ContinentRadius*.5) ||
            Continents>1 && ContinentRadius*2.5>WorldSize*.56*Math.Sin(Math.PI/Continents))
            throw new InvalidDataException("Unsupported macro profile dimensions or feature budget.");
    }
    // Property order is fixed by this version's record; cultures/catalogue order cannot change the digest.
    public string Digest() { Validate(); return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this))).ToLowerInvariant(); }
}

public static class MacroProfileCatalog
{
    public static IReadOnlyList<MacroWorldProfile> Load(string path)
    {
        if(new FileInfo(path).Length>262144) throw new InvalidDataException("Profile catalogue too large.");
        try
        {
            var profiles=JsonSerializer.Deserialize<MacroWorldProfile[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty profile catalogue.");
            if(profiles.Length is <1 or >64 || profiles.Any(p=>p==null) || profiles.Select(p=>p.Id).Distinct().Count()!=profiles.Length)
                throw new InvalidDataException("Invalid or duplicate world profiles.");
            foreach(var profile in profiles) profile.Validate();
            return Array.AsReadOnly(profiles);
        }
        catch(JsonException error) { throw new InvalidDataException("Damaged profile catalogue.",error); }
    }
}

/// <summary>Immutable new-world header. Does not migrate legacy WorldState or cave-lab saves.</summary>
public sealed record MacroWorldManifest
{
    public const int CurrentAlgorithm = 3; // Stable default; experimental versions must be chosen explicitly.
    public const int ExperimentalAlgorithm = 4;
    public required int Format { get; init; }
    public required int Algorithm { get; init; }
    public required string WorldId { get; init; }
    public required int Seed { get; init; }
    public required double OriginX { get; init; }
    public required double OriginZ { get; init; }
    public required string ContentRevision { get; init; }
    public required MacroWorldProfile Profile { get; init; }
    public required string ProfileDigest { get; init; }
    public static MacroWorldManifest Create(int seed,MacroWorldProfile profile,string contentRevision="macro-content-v1",double originX=0,double originZ=0,int algorithm=CurrentAlgorithm)
    {
        var result=new MacroWorldManifest { Format=1,Algorithm=algorithm,WorldId=Guid.NewGuid().ToString("N"),Seed=seed,
            OriginX=originX,OriginZ=originZ,ContentRevision=contentRevision,Profile=profile,ProfileDigest=profile.Digest() };
        result.Validate(); return result;
    }
    public void Validate()
    {
        if(Format!=1 || Algorithm is not (3 or 4) || !Guid.TryParseExact(WorldId,"N",out _) || Profile==null)
            throw new InvalidDataException("Unsupported or damaged macro world manifest.");
        Profile.Validate();
        if(ProfileDigest!=Profile.Digest()) throw new InvalidDataException("Frozen world profile digest does not match.");
        try
        {
            _=new GenerationIdentity(Seed,Algorithm,ContentRevision).FeatureId("manifest",new(0,0),0);
            double half=Profile.WorldSize*.5;
            WorldRegion.At(OriginX-half,OriginZ-half).Validate(); WorldRegion.At(OriginX+half,OriginZ+half).Validate();
        }
        catch(ArgumentException error) { throw new InvalidDataException("Invalid world origin or content revision.",error); }
    }
}

public static class MacroManifestStore
{
    public static void Create(string path,MacroWorldManifest manifest)
    {
        manifest.Validate();
        string target=Path.GetFullPath(path),temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { JsonSerializer.Serialize(stream,manifest,new JsonSerializerOptions { WriteIndented=true }); stream.Flush(true); }
            // A manifest is immutable: an existing world is never silently replaced.
            File.Move(temporary,target,false);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    public static MacroWorldManifest Load(string path)
    {
        if(new FileInfo(path).Length>65536) throw new InvalidDataException("Manifest too large.");
        try
        {
            var manifest=JsonSerializer.Deserialize<MacroWorldManifest>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty manifest.");
            manifest.Validate(); return manifest;
        }
        catch(JsonException error) { throw new InvalidDataException("Damaged manifest.",error); }
    }
}


