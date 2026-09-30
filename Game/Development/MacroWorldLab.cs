using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Thornwake.Core;

namespace Thornwake.Development;

/// <summary>Opt-in macro geography preview. Never reads or migrates the normal player world.</summary>
public partial class MacroWorldLab : Control
{
    private System.Collections.Generic.IReadOnlyList<MacroWorldProfile> _profiles = null!;
    private MacroWorldGenerator _world = null!;
    private OptionButton _profile = null!, _layer = null!, _algorithm = null!;
    private Button _refineButton = null!;
    private SpinBox _seed = null!;
    private TextureRect _map = null!;
    private Label _status = null!, _details = null!, _point = null!, _message = null!;
    private ImageTexture? _texture;
    private sealed record Request(MacroWorldGenerator World,double X,double Z,double Span,MacroMapLayer Layer,int Revision,DrainageAnalysis? Drainage,RegionalDrainageRefiner? Refiner);
    private Request? _pending,_active,_displayed;
    private sealed record PreviewResult(MacroMapResult Map, DrainageAnalysis? Drainage,RegionalDrainageRefiner? Refiner=null,RegionalDrainageTile? Tile=null);
    private Task<PreviewResult>? _job;
    private DrainageAnalysis? _drainage, _displayedDrainage;
    private MacroWorldManifest? _drainageWorld;
    private RegionalDrainageRefiner? _refiner;
    private RegionalDrainageTile? _displayedTile;
    private CancellationTokenSource? _cancel;
    private int _revision,_discarded,_readyFrames,_smokeStage,_smokeRegionalCell;
    private bool _closing,_smoke,_drainageSmoke;
    private double _x,_z,_span,_selectedX,_selectedZ;
    private string _capture="",_manifestPath="";

    public override void _Ready()
    {
        try
        {
            GetTree().AutoAcceptQuit=false;
            _profiles=MacroProfileCatalog.Load(ProjectSettings.GlobalizePath("res://Content/world_profiles.json"));
            string requested="vast-world"; int requestedAlgorithm=MacroWorldManifest.CurrentAlgorithm;
            foreach(string arg in OS.GetCmdlineUserArgs())
            {
                if(arg=="--macro-smoke") _smoke=true;
                if(arg=="--drainage-smoke") { _smoke=true; _drainageSmoke=true; }
                if(arg.StartsWith("--capture=")) _capture=arg[10..];
                if(arg.StartsWith("--manifest=")) _manifestPath=arg[11..];
                if(arg.StartsWith("--profile=")) requested=arg[10..];
                if(arg.StartsWith("--algorithm=")) requestedAlgorithm=int.Parse(arg[12..]);
            }
            if(requestedAlgorithm is not (3 or 4)) throw new InvalidDataException("Unsupported preview algorithm / drainage experiment combination.");
            BuildUi();
            _algorithm.Select(requestedAlgorithm==4?1:0);
            int selected=_profiles.ToList().FindIndex(p=>p.Id==requested);
            if(selected<0) throw new InvalidDataException("Unknown preview profile.");
            _profile.Select(selected); if(_drainageSmoke) _layer.Select((int)MacroMapLayer.DrainageRoutes); Generate();
            GD.Print("MACRO_LAB_READY: isolated world generation preview; legacy worlds unchanged");
        }
        catch(Exception error) { GD.PushError(error.ToString()); Close(1); }
    }
    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background=new ColorRect { Color=new Color("101e27"),MouseFilter=MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(background);
        var margin=new MarginContainer(); margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach(string side in new[]{"left","right","top","bottom"}) margin.AddThemeConstantOverride("margin_"+side,18);
        AddChild(margin); var box=new VBoxContainer(); box.AddThemeConstantOverride("separation",10); margin.AddChild(box);
        var title=new Label { Text="THORNWAKE  /  MACRO WORLD PREVIEW" }; title.AddThemeFontSizeOverride("font_size",23); box.AddChild(title);
        var tools=new HBoxContainer(); tools.AddThemeConstantOverride("separation",10); box.AddChild(tools);
        tools.AddChild(new Label { Text="Seed" });
        _seed=new SpinBox { MinValue=int.MinValue,MaxValue=int.MaxValue,Value=7319,Step=1,CustomMinimumSize=new(160,0) }; tools.AddChild(_seed);
        _profile=new OptionButton(); foreach(var p in _profiles) _profile.AddItem(p.Name); tools.AddChild(_profile);
        _algorithm=new OptionButton(); _algorithm.AddItem("Algorithm 3 (preserved)",3); _algorithm.AddItem("Algorithm 4 (accepted)",4); tools.AddChild(_algorithm);
        void Button(string text,Action action) { var button=new Button { Text=text }; button.Pressed+=action; tools.AddChild(button); }
        Button("Generate",Generate); Button("Save world",Save); Button("Load world",Load);
        _layer=new OptionButton(); _layer.AddItem("Elevation"); _layer.AddItem("Land ownership"); _layer.AddItem("Mountain ranges"); _layer.AddItem("Drainage routes"); _layer.AddItem("Catchments"); _layer.AddItem("Basin candidates"); _layer.AddItem("Regional refinement");
        _layer.ItemSelected+=_=> { if(_layer.Selected==(int)MacroMapLayer.RegionalRefinement) InspectRegion(); else RequestMap(); };
        var layers=new HBoxContainer(); layers.AddChild(new Label { Text="Diagnostic layer" }); layers.AddChild(_layer); box.AddChild(layers);
        var row=new HBoxContainer { SizeFlagsVertical=SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation",18); box.AddChild(row);
        _map=new TextureRect { ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill,CustomMinimumSize=new(640,600) };
        _map.TextureFilter=TextureFilterEnum.Linear; _map.GuiInput+=MapInput; row.AddChild(_map);
        var scroll=new ScrollContainer { CustomMinimumSize=new(320,0),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsVertical=SizeFlags.ExpandFill }; row.AddChild(scroll);
        var sidebar=new VBoxContainer { SizeFlagsHorizontal=SizeFlags.ExpandFill }; sidebar.AddThemeConstantOverride("separation",16); scroll.AddChild(sidebar);
        _status=new Label { AutowrapMode=TextServer.AutowrapMode.WordSmart }; sidebar.AddChild(_status);
        _details=new Label { AutowrapMode=TextServer.AutowrapMode.WordSmart }; sidebar.AddChild(_details);
        _point=new Label { AutowrapMode=TextServer.AutowrapMode.WordSmart,Text="Click the map to inspect a location." }; sidebar.AddChild(_point);
        var navigation=new HBoxContainer(); sidebar.AddChild(navigation);
        void Navigation(string text,Action action) { var b=new Button { Text=text }; b.Pressed+=action; navigation.AddChild(b); }
        Navigation("Zoom in",()=>Zoom(.5)); Navigation("Zoom out",()=>Zoom(2)); Navigation("Whole world",Home);
        _refineButton=new Button { Text="Refine selected cell" }; _refineButton.Pressed+=InspectRegion; sidebar.AddChild(_refineButton); sidebar.MoveChild(navigation,1); sidebar.MoveChild(_refineButton,2);
        sidebar.AddChild(new Label { AutowrapMode=TextServer.AutowrapMode.WordSmart,
            Text="Click: select / centre zoom\nWASD or arrows: pan\nEscape: close\n\nDrainage: yellow arrows = major routes; cyan = receiving ocean outlets. Purple = candidate basins; pink = spill cells. These coarse cells are not river/lake geometry.\n\nThis preview never changes your normal world." });
        _message=new Label { AutowrapMode=TextServer.AutowrapMode.WordSmart }; box.AddChild(_message);
        _message.Text="Choose a profile and seed, then Generate. Saved worlds retain their original profile.";
    }
    private void Generate()
    {
        if(_profiles==null) return;
        _world=new(MacroWorldManifest.Create((int)_seed.Value,_profiles[_profile.Selected],algorithm:_algorithm.GetSelectedId())); ConfigureAlgorithm(); Home();
    }
    private void ConfigureAlgorithm()
    {
        for(int i=(int)MacroMapLayer.DrainageRoutes;i<_layer.ItemCount;i++) _layer.SetItemDisabled(i,false);
        _refineButton.Disabled=false;
        _message.Text=$"Algorithm {_world.Manifest.Algorithm}: {(_world.Manifest.Algorithm==4?"accepted new-world terrain":"preserved historical terrain")}. Drainage is diagnostic metadata, not physical rivers/lakes. Saved worlds keep their own algorithm.";
    }
    private void InspectRegion()
    {
        if(_world==null) return;
        double spacing=_world.Manifest.Profile.WorldSize/128;
        double minX=_world.Manifest.OriginX-_world.Manifest.Profile.WorldSize*.5,minZ=_world.Manifest.OriginZ-_world.Manifest.Profile.WorldSize*.5;
        int cx=Math.Clamp((int)Math.Floor((_selectedX-minX)/spacing),0,127),cz=Math.Clamp((int)Math.Floor((_selectedZ-minZ)/spacing),0,127);
        _x=minX+(cx+.5)*spacing; _z=minZ+(cz+.5)*spacing; _span=spacing;
        _layer.Select((int)MacroMapLayer.RegionalRefinement); RequestMap();
        _message.Text="Regional proposal only: yellow paths, red uphill conflicts, green inlets, cyan continuation, purple lake candidates. Arrows/WASD move one tile; Whole world returns.";
    }
    private void Home()
    {
        _x=_world.Manifest.OriginX; _z=_world.Manifest.OriginZ; _selectedX=_x; _selectedZ=_z;
        _span=_world.Manifest.Profile.WorldSize; if(_layer.Selected==(int)MacroMapLayer.RegionalRefinement) _layer.Select((int)MacroMapLayer.DrainageRoutes); RequestMap();
    }
    private void Zoom(double factor)
    {
        if(_layer.Selected==(int)MacroMapLayer.RegionalRefinement) { _message.Text="Regional inspection shows one fixed tile. Use arrows to inspect its neighbours, or Whole world to return."; return; }
        _span=Math.Clamp(_span*factor,128,_world.Manifest.Profile.WorldSize);
        _x=_selectedX; _z=_selectedZ; ClampCenter(); RequestMap();
    }
    private void ClampCenter()
    {
        double half=_world.Manifest.Profile.WorldSize*.5;
        _x=Math.Clamp(_x,_world.Manifest.OriginX-half,_world.Manifest.OriginX+half);
        _z=Math.Clamp(_z,_world.Manifest.OriginZ-half,_world.Manifest.OriginZ+half);
    }
    private void RequestMap()
    {
        if(_world==null || _closing) return;
        _cancel?.Cancel(); _revision++; _readyFrames=0;
        _pending=new(_world,_x,_z,_span,(MacroMapLayer)_layer.Selected,_revision,_drainageWorld==_world.Manifest?_drainage:null,_refiner?.Recipe.Coarse.World==_world.Manifest?_refiner:null);
        _status.Text="Generating map…\nPrevious view stays visible until ready.";
    }
    // Keep historical v3 paths intact; versioned new worlds must never occupy their save slots.
    private static string WorldPath(int algorithm,string profile,int seed)=>ProjectSettings.GlobalizePath(
        $"user://macro-world-previews/{(algorithm==3?"":"algorithm-"+algorithm+"/")}{profile}/{seed}/manifest.json");
    private string SavePath()=>_manifestPath!=""?_manifestPath:WorldPath(_algorithm.GetSelectedId(),_profiles[_profile.Selected].Id,(int)_seed.Value);
    private void Save()
    {
        try
        {
            // Use the generated world's header, even if the controls contain an ungenerated draft.
            string path=_manifestPath!=""?_manifestPath:WorldPath(_world.Manifest.Algorithm,_world.Manifest.Profile.Id,_world.Manifest.Seed);
            MacroManifestStore.Create(path,_world.Manifest); _message.Text="Saved frozen world: "+path;
        }
        catch(IOException) { _message.Text="A saved world already exists here. Use Load world; Generate never replaces saved geography."; }
        catch(Exception error) { _message.Text="Save failed: "+error.Message; }
    }
    private void Load()
    {
        try
        {
            var manifest=MacroManifestStore.Load(SavePath()); _world=new(manifest);
            _seed.Value=manifest.Seed; int index=_profiles.ToList().FindIndex(p=>p.Id==manifest.Profile.Id); if(index>=0) _profile.Select(index);
            _algorithm.Select(manifest.Algorithm==4?1:0); ConfigureAlgorithm(); Home(); _message.Text=$"Loaded Algorithm {manifest.Algorithm} with its saved profile snapshot; current catalogue changes are not applied.";
        }
        catch(Exception error) { _message.Text="Load failed; preview retained: "+error.Message; }
    }
    public override void _Process(double delta)
    {
        if(_closing) return;
        try
        {
            if(_job is { IsCompleted:true })
            {
                try
                {
                    var result=_job.GetAwaiter().GetResult();
                    if(_active!.Revision==_revision) Commit(result,_active); else _discarded++;
                }
                catch(OperationCanceledException) { _discarded++; }
                finally { _job=null; _cancel?.Dispose(); _cancel=null; }
            }
            if(_job==null && _pending!=null)
            {
                _active=_pending; _pending=null; var request=_active; _cancel=new(); var token=_cancel.Token;
                _job=Task.Run(()=>
                {
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    PreviewResult Done(MacroMapResult map,DrainageAnalysis? data,RegionalDrainageRefiner? refiner=null,RegionalDrainageTile? tile=null)
                        =>new(map with { Milliseconds=watch.Elapsed.TotalMilliseconds },data,refiner,tile);
                    var drainage=request.Drainage;
                    if(request.Layer==MacroMapLayer.RegionalRefinement)
                    {
                        var refiner=request.Refiner??RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(request.World.Manifest),cancellation:token);
                        if(!refiner.Drainage.Grid.TryCell(request.X,request.Z,out int cell)) throw new InvalidOperationException("Regional view is outside its world.");
                        var tile=refiner.Build(cell,token);
                        return Done(RegionalDrainageMap.Build(request.World,tile,320,token),refiner.Drainage,refiner,tile);
                    }
                    if(request.Layer>=MacroMapLayer.DrainageRoutes)
                    {
                        drainage??=MacroDrainage.Build(MacroDrainageRecipe.Create(request.World.Manifest),token);
                        return Done(MacroDrainageMap.Build(request.World,drainage,request.X,request.Z,request.Span,640,request.Layer,token),drainage);
                    }
                    return Done(MacroWorldMap.Build(request.World,request.X,request.Z,request.Span,640,request.Layer,token),drainage);
                },token);
            }
            if(_job==null && _pending==null) _readyFrames++; else _readyFrames=0;
            if(_drainageSmoke && _smokeStage==3 && _readyFrames>1) FinishDrainageSmoke();
            else if(_drainageSmoke && (_smokeStage==4 || _smokeStage==6 || _smokeStage==7 || _smokeStage==8) && _readyFrames>1) FinishRegionalSmoke();
            else if(_smoke) SmokeStep();
            else if(_capture!="" && _readyFrames==5) Capture();
        }
        catch(Exception error) { GD.PushError(error.ToString()); Close(1); }
    }
    private void Commit(PreviewResult result,Request request)
    {
        _point.Text="Click the current map to inspect a location.";
        var map=result.Map; _displayedDrainage=result.Drainage; _displayedTile=result.Tile; if(result.Refiner!=null) _refiner=result.Refiner;
        if(result.Drainage!=null) { _drainage=result.Drainage; _drainageWorld=request.World.Manifest; }
        using var image=Image.CreateFromData(map.Resolution,map.Resolution,false,Image.Format.Rgba8,map.Pixels);
        var texture=ImageTexture.CreateFromImage(image); var old=_texture; _texture=texture; _map.Texture=texture; old?.Dispose(); _displayed=request;
        var p=request.World.Manifest.Profile;
        _status.Text=$"{p.Name}\nAlgorithm {request.World.Manifest.Algorithm}{(request.World.Manifest.Algorithm==4?" / accepted":" / preserved")}\nSeed {request.World.Manifest.Seed} · view {request.Span/1000:0.#} km\nPrepared in {map.Milliseconds:0} ms";
        _details.Text=$"{p.Continents} major land features\n{p.Islands} island candidates\n{request.World.Ranges.Count} mountain belts\n\nSampled view:\n{map.LandFraction:P1} land\n{map.SampledLandComponents} land components\n{map.MinimumHeight:0} to {map.MaximumHeight:0} m\n\nComponent counts depend on zoom and map resolution.";
        if(request.Layer>=MacroMapLayer.DrainageRoutes && result.Drainage is { } d)
            _details.Text=$"COARSE DRAINAGE / v{DrainageAnalysis.Algorithm}\n{d.Grid.Width} x {d.Grid.Depth} cells · {d.Grid.Spacing/1000:0.##} km each\n{d.Basins.Count} basin candidates\n{d.Outlets.Count} sampled ocean outlets\n\nYellow: routes with >= 4 cells' area\nCyan: receiving ocean outlets\nPurple/pink: basin/spill cells\n\nArea is not discharge. Spill height is not water level.\nFingerprint: {d.Fingerprint[..12]}";
        if(result.Tile is { } tile)
        {
            _details.Text=$"REGIONAL PROPOSAL / v{RegionalDrainageRefiner.Algorithm}\nCell {tile.Cell} · order {tile.RiverOrder}\n{tile.Side} x {tile.Side} nodes · {tile.SampleSpacing:0.##} m spacing\n{tile.Routes.Count} tributary paths\nNext cell: {tile.Outgoing?.ToCell.ToString()??"ocean"}\nDestination: {tile.DestinationCell}\n\nYellow: proposed corridor\nRed: uphill terrain conflict\nGreen: inlet / cyan: continuation\n\nDownhill-channel cut estimate: {tile.Routes.Select(r=>r.DownhillCutEstimate).DefaultIfEmpty().Max():0.0} m\nBed rises are not water flow. No terrain has been carved.";
            if(tile.Lake is { } lake) _details.Text+=$"\n\nLake candidate: {lake.InteriorCells.Count} interior cells\n{lake.Components} connected footprint components\n{lake.UnresolvedShoreCells} unresolved shore cells\nCandidate spill {lake.CandidateSpillElevation:0.0} m\nPatch capacity {lake.CapacityEstimate/1e6:0.##} million m³\nPatch links: {lake.Inlets.Count} inlets / {lake.Outlets.Count} external outflows\n{lake.BlockedOutletCount} outflow ports above candidate water";
        }
    }
    private void MapInput(InputEvent input)
    {
        if(input is not InputEventMouseButton { Pressed:true,ButtonIndex:MouseButton.Left } click || _displayed==null) return;
        float size=Math.Min(_map.Size.X,_map.Size.Y); var offset=(_map.Size-new Vector2(size,size))*.5f; var uv=(click.Position-offset)/size;
        if(uv.X<0 || uv.X>1 || uv.Y<0 || uv.Y>1) return;
        var view=_displayed; _selectedX=view.X+(uv.X-.5)*view.Span; _selectedZ=view.Z+(uv.Y-.5)*view.Span;
        var sample=view.World.Sample(_selectedX,_selectedZ); var region=WorldRegion.At(_selectedX,_selectedZ);
        _point.Text=$"Selected location\nX {_selectedX:0.0} m\nZ {_selectedZ:0.0} m\nElevation {sample.Elevation:0.0} m\nRegion ({region.X}, {region.Z})";
        if(_displayedDrainage?.TryDescribeCell(_selectedX,_selectedZ,out var info)==true)
        {
            _point.Text+=$"\n\nCoarse cell {info!.Cell}\nBed {info.BedElevation:0.0} m / spill {info.SpillElevation:0.0} m\nArea {info.CatchmentArea/1e6:0.##} km²\nUpstream: {string.Join(", ",info.UpstreamCells)}\nNext: {info.DownstreamCell?.ToString()??"ocean outlet"}\nDestination: {info.OutletCell}";
            int basin=_displayedDrainage.BasinIndex[info.Cell];
            if(basin>=0)
            {
                var b=_displayedDrainage.Basins[basin];
                _point.Text+=$"\nBasin {b.Cells[0]} · {b.Cells.Count} cells\nCandidate capacity {b.CapacityEstimate/1e6:0.##} million m³\nSpills: {string.Join(", ",b.Exits.Take(4).Select(e=>$"{e.From} -> {e.To}"))}";
            }
            else _point.Text+="\nNo basin candidate in this cell.";
        }
    }
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if(input is not InputEventKey { Pressed:true } key) return;
        if(key.Keycode==Key.Escape) { Close(); return; }
        double step=_span*(_layer.Selected==(int)MacroMapLayer.RegionalRefinement?1:.15);
        switch(key.Keycode)
        {
            case Key.W: case Key.Up: _z-=step; break; case Key.S: case Key.Down: _z+=step; break;
            case Key.A: case Key.Left: _x-=step; break; case Key.D: case Key.Right: _x+=step; break; default:return;
        }
        ClampCenter(); _selectedX=_x; _selectedZ=_z; if(_layer.Selected==(int)MacroMapLayer.RegionalRefinement) InspectRegion(); else RequestMap();
    }
    private void SmokeStep()
    {
        if(_smokeStage==0 && _readyFrames>1)
        {
            if(_manifestPath=="") throw new InvalidOperationException("Smoke test requires an isolated manifest path.");
            var point=_world.LandFeatures[0].Center; var before=_world.Sample(point.X,point.Z);
            MacroManifestStore.Create(_manifestPath,_world.Manifest); var restored=new MacroWorldGenerator(MacroManifestStore.Load(_manifestPath));
            if(restored.Manifest!=_world.Manifest || restored.Sample(point.X,point.Z)!=before) throw new Exception("Preview save reconstruction failed.");
            _world=restored;
            if(WorldPath(3,_world.Manifest.Profile.Id,_world.Manifest.Seed)==WorldPath(4,_world.Manifest.Profile.Id,_world.Manifest.Seed))
                throw new Exception("Experimental save slot aliases stable world.");
            _algorithm.Select(_world.Manifest.Algorithm==4?0:1); Load();
            if(_algorithm.GetSelectedId()!=restored.Manifest.Algorithm || _world.Manifest!=restored.Manifest || _refineButton.Disabled)
                throw new Exception("Saved algorithm / preview capability restoration failed.");
            float size=Math.Min(_map.Size.X,_map.Size.Y); var offset=(_map.Size-new Vector2(size,size))*.5f;
            using(var click=new InputEventMouseButton { Pressed=true,ButtonIndex=MouseButton.Left,Position=offset+new Vector2(size*.75f,size*.25f) }) MapInput(click);
            double expectedX=_world.Manifest.OriginX+_span*.25,expectedZ=_world.Manifest.OriginZ-_span*.25,oldSpan=_span;
            Zoom(.5);
            if(Math.Abs(_x-expectedX)>.01 || Math.Abs(_z-expectedZ)>.01 || _span!=oldSpan*.5) throw new Exception("Map selection/zoom failed.");
            _smokeStage=1;
        }
        else if(_smokeStage==1 && _job!=null) { Home(); _smokeStage=2; }
        else if(_smokeStage==2 && _readyFrames>1)
        {
            if(_discarded<1) throw new Exception("Stale preview was not rejected.");
            if(_drainageSmoke)
            {
                if(_displayedDrainage==null || _drainageWorld!=_world.Manifest) throw new Exception("Drainage preview lost its world identity.");
                _layer.Select((int)MacroMapLayer.DrainageBasins); RequestMap(); _smokeStage=3; return;
            }
            GD.Print("MACRO_LAB_PASS: generated map, selection/zoom, frozen manifest reload, bounded worker, obsolete view rejected"); _smokeStage=3; Capture();
        }
    }
    private void FinishDrainageSmoke()
    {
        if(_displayedDrainage==null || _displayed?.Layer!=MacroMapLayer.DrainageBasins) throw new Exception("Drainage layer switch failed.");
        var restored=MacroDrainage.Build(MacroDrainageRecipe.Create(MacroManifestStore.Load(_manifestPath)));
        if(restored.Fingerprint!=_displayedDrainage.Fingerprint) throw new Exception("Drainage preview reconstruction changed.");
        float size=Math.Min(_map.Size.X,_map.Size.Y); var offset=(_map.Size-new Vector2(size,size))*.5f;
        using(var click=new InputEventMouseButton { Pressed=true,ButtonIndex=MouseButton.Left,Position=offset+new Vector2(size*.5f,size*.5f) }) MapInput(click);
        if(!_point.Text.Contains("Coarse cell")) throw new Exception("Drainage selection failed.");
        int candidate=_displayedDrainage.Basins.OrderByDescending(b=>b.CapacityEstimate).First().Cells[0];
        var selected=_displayedDrainage.Grid.Center(candidate); _selectedX=selected.X; _selectedZ=selected.Z;
        InspectRegion(); _smokeStage=4;
        GD.Print("DRAINAGE_LAB_PASS: routing/basin layers, cell query, zoom, immutable reload and stale worker rejection");
    }
    private void FinishRegionalSmoke()
    {
        if(_smokeStage==6)
        {
            if(_displayedDrainage==null || _displayed!.World.Manifest!=_world.Manifest || _drainageWorld!=_world.Manifest ||
                _world.Manifest.WorldId==MacroManifestStore.Load(_manifestPath).WorldId) throw new Exception("Changed-world drainage reused stale data.");
            _layer.Select((int)MacroMapLayer.DrainageCatchments); Load(); _smokeStage=7; return;
        }
        if(_smokeStage==7)
        {
            if(_displayed?.Layer!=MacroMapLayer.DrainageCatchments || _drainageWorld!=_world.Manifest || _world.Manifest!=MacroManifestStore.Load(_manifestPath))
                throw new Exception("Catchment layer or world reload failed.");
            var point=_displayedDrainage!.Grid.Center(_smokeRegionalCell); _selectedX=point.X; _selectedZ=point.Z;
            InspectRegion(); _smokeStage=8; return;
        }
        if(_displayedTile==null || _displayed?.Layer!=MacroMapLayer.RegionalRefinement || _displayedTile.Lake==null)
            throw new Exception("Regional diagnostic view failed.");
        if(_smokeStage==4)
        {
            _smokeRegionalCell=_displayedTile.Cell;
            _profile.Select((_profile.Selected+1)%_profiles.Count); Generate(); _smokeStage=6; return;
        }
        if(_displayedTile.Cell!=_smokeRegionalCell) throw new Exception("Regional cell changed on world reconstruction.");
        GD.Print($"REGIONAL_LAB_PASS: {_displayedTile.Cell}, {_displayedTile.Routes.Count} paths, {_displayedTile.SampleSpacing} m nodes, {_displayedTile.Lake.InteriorCells.Count} lake samples; layer/world switches and reload passed");
        _smokeStage=9; Capture();
    }

    private async void Capture()
    {
        try
        {
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(_capture!="")
            {
                using var image=GetViewport().GetTexture().GetImage();
                if(image.SavePng(_capture)!=Error.Ok) throw new IOException("Preview capture failed.");
                GD.Print("MACRO_LAB_CAPTURED "+_capture);
            }
            Close();
        }
        catch(Exception error) { GD.PushError(error.ToString()); Close(1); }
    }
    public override void _Notification(int what) { if(what==NotificationWMCloseRequest) Close(); }
    private async void Close(int code=0)
    {
        if(_closing) return; _closing=true; _cancel?.Cancel(); _pending=null; SetProcess(false);
        if(_job!=null) _=_job.ContinueWith(t=>{ _=t.Exception; },TaskScheduler.Default);
        foreach(var child in GetChildren()) child.QueueFree();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        _texture?.Dispose(); _texture=null;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); GC.WaitForPendingFinalizers(); GetTree().Quit(code);
    }
}









