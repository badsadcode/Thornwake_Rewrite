using Thornwake.Core;

internal static class CameraTests
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception("CAMERA: "+message);checks++;}
        var slow=new OrbitState();var fast=new OrbitState();slow.Look(150,-40,1,false);fast.Look(150,-40,1,false);
        Check(slow.Yaw==-.35f,"mouse changes target without snapping rendered yaw");
        for(int i=0;i<30;i++)slow.Step(1f/30);
        for(int i=0;i<144;i++)fast.Step(1f/144);
        Check(Math.Abs(slow.Yaw-fast.Yaw)<.0001f&&Math.Abs(slow.Pitch-fast.Pitch)<.0001f,"same response at 30 and 144 FPS");
        var orbit=new OrbitState();orbit.Look(200000,200000,2.5f,false);orbit.Zoom(10000);
        for(int i=0;i<300;i++)orbit.Step(1f/60);
        Check(float.IsFinite(orbit.Yaw)&&orbit.Pitch<=1.2f&&orbit.Distance<=48,"sustained input keeps orbit finite and clamped");
        orbit.Look(0,-200000,1,false);orbit.Zoom(-200000);for(int i=0;i<300;i++)orbit.Step(1f/60);
        Check(orbit.Pitch>=.31999f&&orbit.Distance>=13.999f,"lower pitch and zoom limits");
        var normal=new OrbitState();var inverted=new OrbitState();normal.Look(0,40,1,false);inverted.Look(0,40,1,true);
        Check(normal.TargetPitch>OrbitState.DefaultPitch&&inverted.TargetPitch<OrbitState.DefaultPitch,"invert changes vertical direction");
        orbit.Reset();for(int i=0;i<180;i++)orbit.Step(1f/60);
        Check(Math.Abs(orbit.Yaw+.35)<.001&&Math.Abs(orbit.Pitch-OrbitState.DefaultPitch)<.001,"reset recovers view smoothly after repeated rotations");
        var pointer=new PointerGestures();pointer.Button(PointerButton.Left,true,true);pointer.Tick(.02f);pointer.Button(PointerButton.Right,true,true);
        Check(pointer.Orbiting&&!pointer.TakePrimary()&&!pointer.Blocking,"left-then-right starts orbit without swinging or blocking");
        pointer.Tick(.3f);pointer.Button(PointerButton.Left,false,true);
        Check(!pointer.Orbiting&&!pointer.Blocking&&!pointer.TakePrimary(),"ending a chord does not become a click or block");
        pointer.Button(PointerButton.Right,false,true);Check(!pointer.TakeSecondary(),"ending a chord does not cancel building");
        pointer.Button(PointerButton.Right,true,true);Check(pointer.Blocking,"ordinary right hold blocks immediately");
        pointer.Button(PointerButton.Left,true,true);Check(pointer.Orbiting&&!pointer.TakePrimary(),"right-then-left also starts orbit safely");
        pointer.Cancel();pointer.Button(PointerButton.Left,true,true);pointer.Button(PointerButton.Left,false,true);
        Check(pointer.TakePrimary()&&!pointer.TakePrimary(),"quick click executes exactly once");
        pointer.Button(PointerButton.Left,true,true);pointer.Tick(.1f);Check(pointer.TakePrimary(),"held ordinary click fires after chord grace period");
        pointer.Tick(1);pointer.Button(PointerButton.Left,false,true);Check(!pointer.TakePrimary(),"release does not repeat a held attack");
        pointer.Cancel();pointer.Button(PointerButton.Middle,true,true);pointer.Button(PointerButton.Right,true,true);pointer.Button(PointerButton.Left,true,true);
        Check(pointer.Orbiting&&pointer.Blocking&&pointer.TakePrimary(),"middle drag supports independent combat buttons");
        pointer.Cancel();pointer.Button(PointerButton.Left,true,false);Check(pointer.TakePrimary(),"always-look attacks have no chord delay");
        pointer.Button(PointerButton.Right,true,false);Check(pointer.Blocking&&!pointer.Orbiting,"always mode preserves combat input");
        pointer.Cancel();Check(!pointer.Blocking&&!pointer.Orbiting&&!pointer.TakePrimary(),"menu/focus cancellation clears pending actions");
        pointer.Button(PointerButton.Right,false,true);Check(!pointer.TakeSecondary(),"unmatched release cannot cancel placement");
        return checks;
    }
}
