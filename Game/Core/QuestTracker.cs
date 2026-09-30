using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.Core;

public sealed record QuestDefinition(string Id,string Name,string Description,string Objective);
public static class QuestTracker
{
    public static IReadOnlyList<QuestDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new QuestDefinition("gather_resources","The valley provides","Get to know the materials around Harthwood.","Gather a tree, stone or berry bush."),
        new QuestDefinition("craft_axe","A better tool","Turn the valley's materials into an axe of your own.","Finish crafting a stone axe."),
        new QuestDefinition("build_shelter","A place of your own","Raise a shelter with a supported frame.","Build a floor, supporting wall and roof together."),
        new QuestDefinition("clear_road","A safer road","The bandit beyond the woods is holding up the caravans.","Defeat the road bandit.")
    });
    public static void RecordCraft(WorldState state,string output)
    {if(output=="axe")state.CompletedQuests.Add("craft_axe");}
    public static void Refresh(WorldState state)
    {
        if(state.RemovedResources.Count>0)state.CompletedQuests.Add("gather_resources");
        if(state.EnemyDefeated)state.CompletedQuests.Add("clear_road");
        if(state.Structures.Any(roof=>Construction.IsRoof(roof)&&state.Structures.Any(floor=>Construction.IsFloor(floor)&&
            (roof.SupportId==floor.Id||roof.SupportId==""&&Construction.OnFloor(floor,roof.X,roof.Z))&&Construction.HasFrameSupport(state,floor))))state.CompletedQuests.Add("build_shelter");
    }
}
