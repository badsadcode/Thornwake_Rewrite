using Godot;
using System;
using System.Collections.Generic;
using Thornwake.Core;

namespace Thornwake.UI;

/// <summary>A north-up field map; world coordinates use X and Z as its two axes.</summary>
public partial class ValleyMap : Control
{
    public Vector2 PlayerPosition;
    public bool Expanded;
    public int Seed = 7319;
    private bool _lastExpanded;
    private WorldState? _world;
    private ImageTexture? _terrain;
    private List<ResourceSpawn> _resources=new();
    public void SetWorld(WorldState world){_world=world;_resources=WorldGenerator.Generate(world);RefreshTerrain();}
    public void RefreshTerrain(){if(_world==null)return;var old=_terrain;_terrain=TerrainMapImage.Create(_world);old?.Dispose();QueueRedraw();}
    public override void _ExitTree(){_terrain?.Dispose();_terrain=null;}

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;TextureFilter=TextureFilterEnum.Nearest;
        ApplyLayout();
    }

    public override void _Process(double delta)
    {
        if (_lastExpanded != Expanded) ApplyLayout();
        QueueRedraw();
    }

    private void ApplyLayout()
    {
        _lastExpanded = Expanded;
        AnchorLeft = AnchorRight = Expanded ? .5f : 1;
        AnchorTop = AnchorBottom = Expanded ? .5f : 0;
        OffsetLeft = Expanded ? -260 : -210;
        OffsetTop = Expanded ? -260 : 334;
        OffsetRight = Expanded ? 260 : -24;
        OffsetBottom = Expanded ? 260 : 520;
    }

    public override void _Draw()
    {
        float edge = Expanded ? 520 : 186;
        float mapSide = Expanded ? 448 : 148;
        Vector2 origin = new((edge - mapSide) * .5f, Expanded ? 42 : 22);
        float scale = mapSide / 110f;
        Vector2 Map(Vector2 world) => origin + (world + new Vector2(55, 55)) * scale;
        DrawRect(new Rect2(Vector2.Zero, new Vector2(edge, edge)), new Color("171c1bf0"));
        DrawRect(new Rect2(Vector2.One, new Vector2(edge - 2, edge - 2)), new Color("58604b"), false, 1);
        DrawRect(new Rect2(origin, new Vector2(mapSide, mapSide)), new Color("536346"));

        if(_terrain!=null)DrawTextureRect(_terrain,new Rect2(origin,new Vector2(mapSide,mapSide)),false);
        foreach(var tree in _resources)if(tree.Kind=="tree"&&_world?.RemovedResources.Contains(tree.Id)!=true)
        {
            DrawCircle(Map(new Vector2(tree.X,tree.Z)),1.1f*scale,new Color("354e36"));
            DrawCircle(Map(new Vector2(tree.X,tree.Z))+new Vector2(-.3f,-.3f)*scale,.5f*scale,new Color("607550"));
        }

        if(_world!=null)foreach(var plant in _world.Vegetation)if(plant.Kind=="tree"&&!plant.IsStump)DrawCircle(Map(new Vector2(plant.X,plant.Z)),(.25f+.75f*(float)plant.Growth)*scale,new Color("698751"));

        // The crossing and reserved settlement lots match the playable valley.
        DrawLine(Map(new Vector2(-55, 0)), Map(new Vector2(55, 0)), new Color("a28e67"), 2.7f * scale, true);
        DrawLine(Map(new Vector2(0, -55)), Map(new Vector2(0, 55)), new Color("a28e67"), 2.7f * scale, true);
        DrawLine(Map(new Vector2(27, -55)), Map(new Vector2(27, 55)), new Color("658c8c"), 8 * scale, true);
        DrawLine(Map(new Vector2(23, 0)), Map(new Vector2(31, 0)), new Color("d5bc89"), 3.4f * scale, true);
        foreach (var p in new[] { new Vector2(-9, -8), new Vector2(9, -9), new Vector2(-11, 9), new Vector2(10, 10) })
        {
            DrawRect(new Rect2(Map(p - new Vector2(3, 2.5f)), new Vector2(6, 5) * scale), new Color("d0b377"));
            DrawRect(new Rect2(Map(p - new Vector2(3, 2.5f)), new Vector2(6, 5) * scale), new Color("625944"), false, 1);
        }
        DrawCircle(Map(new Vector2(-29, -23)), 2 * scale, new Color("a5634c"));
        DrawRect(new Rect2(Map(new Vector2(16, 7)), new Vector2(7, 6) * scale), new Color("899554"));

        var clamped = new Vector2(Mathf.Clamp(PlayerPosition.X, -53, 53), Mathf.Clamp(PlayerPosition.Y, -53, 53));
        Vector2 player = Map(clamped);
        float marker = Expanded ? 8 : 5;
        DrawCircle(player, marker + 2, new Color("19241a"));
        DrawColoredPolygon(new[] { player + new Vector2(0, -marker), player + new Vector2(marker * .8f, marker), player + new Vector2(0, marker * .55f), player + new Vector2(-marker * .8f, marker) }, new Color("faf3d8"));

        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(Expanded ? 35 : 15, Expanded ? 27 : 15), Expanded ? "HARTHWOOD VALLEY" : "HARTHWOOD", HorizontalAlignment.Left, -1, Expanded ? 15 : 10, new Color("d8c795"));
        DrawString(font, new Vector2(edge - (Expanded ? 48 : 26), Expanded ? 27 : 15), "N", HorizontalAlignment.Left, -1, Expanded ? 15 : 10, new Color("e9e4d2"));
        DrawString(font, new Vector2(0, edge - (Expanded ? 10 : 5)), Expanded ? "YOU   /   Cottages   /   River crossing       [M] Close map" : "[M] Map", HorizontalAlignment.Center, edge, Expanded ? 12 : 9, new Color("a6b0a2"));
        if (Expanded)
        {
            DrawString(font, Map(new Vector2(-18, -33)), "Old camp", HorizontalAlignment.Left, -1, 12, new Color("e9d6b0"));
            DrawString(font, Map(new Vector2(31, 6)), "Crossing", HorizontalAlignment.Left, -1, 12, new Color("d9e3d4"));
            DrawString(font, Map(new Vector2(-11, 18)), "Harthwood", HorizontalAlignment.Left, -1, 13, new Color("f1dfb5"));
        }
    }
}
