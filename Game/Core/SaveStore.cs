using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Thornwake.Core;

public static class SaveStore
{
    private const int MaximumSaveBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(string path, WorldState state)
    {
        Validate(state);
        string absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        string temporary = absolute + ".tmp";
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(state, Options);
        if (data.Length > MaximumSaveBytes) throw new InvalidDataException("This save exceeds the supported size.");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            // Replace is atomic on the target filesystem. A previous valid save remains available.
            if (File.Exists(absolute))
            {
                // Keep each original older schema even after later autosaves rotate .bak.
                {
                    try{using var previous=JsonDocument.Parse(File.ReadAllText(absolute));if(previous.RootElement.TryGetProperty("SaveVersion",out var old)&&old.TryGetInt32(out int version)&&version<10&&version>=1&&!File.Exists(absolute+".v"+version))File.Copy(absolute,absolute+".v"+version,false);}
                    catch(JsonException){/* A damaged previous file is still retained by atomic replacement. */}
                }
                File.Replace(temporary, absolute, absolute + ".bak");
            }
            else File.Move(temporary, absolute);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static WorldState Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("No saved world exists yet.", path);
        if (info.Length > MaximumSaveBytes) throw new InvalidDataException("This save exceeds the supported size.");
        try
        {
            string json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("SaveVersion", out var version) || !version.TryGetInt32(out int format) || format is <1 or >10)
                throw new InvalidDataException("This world uses an unsupported save version.");
            foreach (string required in new[] { "GeneratorVersion", "Seed", "Time", "Player", "RemovedResources", "Structures", "TerrainEdits", "Crops", "EnemyDefeated", "Settlement", "Journal" })
                if (!document.RootElement.TryGetProperty(required, out _)) throw new InvalidDataException($"The world save is missing {required}.");
            if(format>=4&&!document.RootElement.TryGetProperty("CraftQueue",out _))throw new InvalidDataException("The world save is missing CraftQueue.");
            if(format>=6)
            {
                var savedPlayer=document.RootElement.GetProperty("Player");
                if(!document.RootElement.TryGetProperty("CompletedQuests",out _)||savedPlayer.ValueKind!=JsonValueKind.Object||!savedPlayer.TryGetProperty("Hotbar",out _)||!savedPlayer.TryGetProperty("EquippedItem",out _)||!savedPlayer.TryGetProperty("Cooldowns",out _))throw new InvalidDataException("The world save is missing its hotbar or quest state.");
            }
            if(format>=7)foreach(string field in new[]{"CalendarMinutes","Settings","Vegetation","RemovedStumps"})if(!document.RootElement.TryGetProperty(field,out _))throw new InvalidDataException("The save is missing "+field+".");
            if(format>=9&&(!document.RootElement.TryGetProperty("Earthworks",out _)||!document.RootElement.TryGetProperty("EarthworkJob",out _)))throw new InvalidDataException("The save is missing its earthworks state.");
            var state = JsonSerializer.Deserialize<WorldState>(json, Options) ?? throw new InvalidDataException("The world save is empty.");
            // Version 1 pieces remain free-standing. New structural metadata is additive.
            if(format==1){state.SaveVersion=2;foreach(var s in state.Structures??new()){if(s!=null){s.Modular=false;s.SupportId="";s.Elevation=null;s.DoorOpen=false;s.PaidCosts=new();}}}
            if(format<=2){state.SaveVersion=3;state.CraftJob=null;if(state.Player!=null)state.Player.Y=null;foreach(var s in state.Structures??new())if(s!=null)s.Level=0;}
            if(format<=3){state.SaveVersion=4;state.CraftQueue=new();if(state.CraftJob!=null)state.CraftJob.QueueEntryId="";}
            if(format<=4){state.SaveVersion=5;foreach(var piece in state.Structures??new())if(piece!=null){piece.Selection=null;piece.RoofConnectionId="";}}
            if(format<=5)
            {
                state.SaveVersion=6;state.CompletedQuests=new();
                if(state.Player!=null){state.Player.Hotbar=HotbarRules.DefaultBindings();state.Player.EquippedItem="basic_axe";state.Player.Cooldowns=new();if(state.Player.Inventory!=null)HotbarRules.AddStarterItems(state.Player);}
            }
            if(format<=6){state.SaveVersion=7;state.Settings=new();state.Vegetation=new();state.RemovedStumps=new();state.CalendarMinutes=480+state.Time*3;}
            if(format<=7){state.SaveVersion=8;foreach(var piece in state.Structures??new())if(piece?.Selection!=null)piece.Selection.AppearanceId="";}
            if(format<=8){state.SaveVersion=9;state.Earthworks=new();state.EarthworkJob=null;}
            if(format<=9){if(state.GeneratorVersion!=1)throw new InvalidDataException("Older saves must use their original generator.");state.SaveVersion=10;state.Generation=new();}
            if(format>=10&&!document.RootElement.TryGetProperty("Generation",out _))throw new InvalidDataException("The save is missing its generation settings.");
            Validate(state);
            return state;
        }
        catch (JsonException exception) { throw new InvalidDataException("The world save is damaged or contains invalid data.", exception); }
    }

    private static void Validate(WorldState state)
    {
        if (state is null || state.SaveVersion != 10 || state.GeneratorVersion is not (1 or 2) || !double.IsFinite(state.Time) || state.Time < 0 || state.Time > 1e10)
            throw new InvalidDataException("Invalid world header or unsupported save version.");
        LandscapeGenerator.Validate(state);
        var player = state.Player;
        if (player is null || !Position(player.X, player.Z) || player.Y.HasValue&&(!float.IsFinite(player.Y.Value)||Math.Abs(player.Y.Value)>50) || !Meter(player.Health) || !Meter(player.Stamina) || !Counts(player.Inventory) || !Counts(player.Skills))
            throw new InvalidDataException("Invalid player state.");
        if(player.Hotbar==null||player.Hotbar.Count!=HotbarRules.SlotCount||player.Hotbar.Any(b=>b!=null&&(b.Kind is not ("item" or "skill")||!Identifier(b.Id)))||player.EquippedItem==null||player.EquippedItem.Length>200||player.Cooldowns==null||player.Cooldowns.Count>200||player.Cooldowns.Any(c=>!CooldownKey(c.Key)||!float.IsFinite(c.Value)||c.Value<0||c.Value>86400))throw new InvalidDataException("Invalid hotbar, equipment or cooldown state.");
        if(state.CompletedQuests==null||state.CompletedQuests.Count>1000||state.CompletedQuests.Any(id=>!Identifier(id)||!QuestTracker.Definitions.Any(q=>q.Id==id)))throw new InvalidDataException("Invalid completed quest state.");
        if (state.RemovedResources is null || state.Structures is null || state.TerrainEdits is null || state.Crops is null || state.Settlement is null || state.Journal is null || state.CraftQueue is null)
            throw new InvalidDataException("A world state collection is missing.");
        if (state.RemovedResources.Count > 10000 || state.Structures.Count > 10000 || state.TerrainEdits.Count > 20000 || state.Crops.Count > 10000 || state.Journal.Count > 1000)
            throw new InvalidDataException("A world collection exceeds the supported limit.");
        if (state.RemovedResources.Any(x => !Identifier(x)) || state.Journal.Any(x => x is null || x.Length > 2000))
            throw new InvalidDataException("Invalid resource identifier or journal entry.");
        if (state.Structures.Any(x => x is null || !Identifier(x.Id) || !Identifier(x.Kind) || !Position(x.X, x.Z) || !float.IsFinite(x.Rotation)) || state.Structures.Select(x => x.Id).Distinct().Count() != state.Structures.Count)
            throw new InvalidDataException("Invalid or duplicate building state.");
        foreach(var s in state.Structures)
        {
            if(s.Level<0||s.Level>2)throw new InvalidDataException("Invalid building level.");
            if(s.SupportId==null||s.SupportId.Length>200||s.PaidCosts==null||!Counts(s.PaidCosts)||s.Elevation.HasValue&&(!float.IsFinite(s.Elevation.Value)||Math.Abs(s.Elevation.Value)>50)||s.Modular&&!s.Elevation.HasValue)
                throw new InvalidDataException("Invalid structural metadata.");
            if(s.SupportId!=""&&!state.Structures.Any(f=>f.Id==s.SupportId&&Construction.IsFloor(f)&&f.Id!=s.Id))throw new InvalidDataException("A structure has lost its floor reference.");
            if(s.RoofConnectionId==null||s.RoofConnectionId.Length>200||s.Selection is {} choice&&(!Identifier(choice.DefinitionId)||!Identifier(choice.VariantId)||!Identifier(choice.MaterialId)||choice.AppearanceId==null||choice.AppearanceId.Length>100||!float.IsFinite(choice.Slope)||choice.Slope<0||choice.Slope>60)||s.Kind=="roof_module"&&(s.Selection==null||!s.Modular||s.SupportId==""))throw new InvalidDataException("Invalid building selection or roof connection.");
            if(s.RoofConnectionId!=""&&(s.Kind!="roof_module"||!state.Structures.Any(a=>a.Id==s.RoofConnectionId&&a.Id!=s.Id&&a.Kind=="roof_module"&&a.Selection!=null&&a.Level==s.Level)))throw new InvalidDataException("A roof has lost its connection reference.");
        }
        RoofGeometry.ValidateReferenceGraph(state);
        if(state.CraftJob is {} job&&(!Identifier(job.RecipeId)||!Identifier(job.StationId)||!float.IsFinite(job.Duration)||job.Duration<.1f||job.Duration>6000||!float.IsFinite(job.Remaining)||job.Remaining<=0||job.Remaining>job.Duration||!Counts(job.PaidCosts)))throw new InvalidDataException("Invalid crafting task.");
        if(state.CraftQueue.Count>50||state.CraftQueue.Any(q=>q is null||!Identifier(q.Id)||!Identifier(q.RecipeId)||!Identifier(q.StationId)||q.RemainingCount<0||q.RemainingCount>999||q.UntilMaterials&&q.RemainingCount!=0||!q.UntilMaterials&&q.RemainingCount==0&&state.CraftJob?.QueueEntryId!=q.Id)||state.CraftQueue.Select(q=>q.Id).Distinct().Count()!=state.CraftQueue.Count)
            throw new InvalidDataException("Invalid crafting queue.");
        if(state.CraftJob is {} active&&(active.QueueEntryId==null||active.QueueEntryId.Length>200||active.QueueEntryId!=""&&(state.CraftQueue.Count==0||state.CraftQueue[0].Id!=active.QueueEntryId||state.CraftQueue[0].RecipeId!=active.RecipeId||state.CraftQueue[0].StationId!=active.StationId)))
            throw new InvalidDataException("The active crafting task has an invalid queue reference.");
        if (state.TerrainEdits.Any(x => x is null || !Position(x.X, x.Z) || !float.IsFinite(x.Radius) || x.Radius <= 0 || x.Radius > 20 || !float.IsFinite(x.Delta) || Math.Abs(x.Delta) > 5))
            throw new InvalidDataException("Invalid terrain edit.");
        if (state.Crops.Any(x => x is null || !Identifier(x.Id) || !Position(x.X, x.Z) || !double.IsFinite(x.PlantedAt) || x.PlantedAt < 0 || x.PlantedAt > state.Time) || state.Crops.Select(x => x.Id).Distinct().Count() != state.Crops.Count)
            throw new InvalidDataException("Invalid or duplicate crop state.");
        TerrainGrid.Validate(state);
        Vegetation.Validate(state);
        var town = state.Settlement;
        if (town.Food is < 0 or > 200 || town.Timber is < 0 or > 200 || town.Prosperity is < 0 or > 100 || town.CaravanArrivals < 0)
            throw new InvalidDataException("Invalid settlement state.");
    }

    private static bool Position(float x, float z) => float.IsFinite(x) && float.IsFinite(z) && Math.Abs(x) <= 60 && Math.Abs(z) <= 60;
    private static bool Meter(float value) => float.IsFinite(value) && value >= 0 && value <= 100;
    private static bool Identifier(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200;
    private static bool CooldownKey(string value)
    {int split=value?.IndexOf(':')??-1;return split>0&&value!.LastIndexOf(':')==split&&(value[..split] is "item" or "skill")&&Identifier(value[(split+1)..]);}
    private static bool Counts(Dictionary<string, int> values) => values is not null && values.Count <= 5000 && values.All(x => Identifier(x.Key) && x.Value >= 0 && x.Value <= 10000000);
}
