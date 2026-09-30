using Godot;
using System;
using System.Linq;
using System.Text.Json;
using Thornwake.Core;

namespace Thornwake;

public partial class Main
{
    private void VerifyPlayerMenus()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception("Player menus: "+message);}
        var previous=_state;
        CreateWorld(new WorldState());_hud.ShowGame();_cameraRig.SetFocused(true);
        Command("hotbar:swap:1:9");
        _UnhandledInput(new InputEventKey{Keycode=Key.Key0,Pressed=true});
        Check(_slot==1&&_state.Player.EquippedItem=="hammer","0 equips hammer after reorder");
        _UnhandledInput(new InputEventKey{Keycode=Key.B,Pressed=true});
        Check(_hud.IsBuildingOpen,"reordered hammer permits building");_hud.HidePanel();
        _UnhandledInput(new InputEventKey{Keycode=Key.Key1,Pressed=true});
        _UnhandledInput(new InputEventKey{Keycode=Key.Key2,Pressed=true});
        Check(_slot==0,"empty shortcut leaves current equipment intact");
        _UnhandledInput(new InputEventKey{Keycode=Key.B,Pressed=true});Check(!_hud.IsPanelOpen,"B requires actual equipped hammer");
        _state.Player.Inventory["bread"]=2;_state.Player.Health=20;_state.Player.Stamina=20;
        int berries=_sim.Count("berries");
        _UnhandledInput(new InputEventKey{Keycode=Key.Key6,Pressed=true});
        Check(_sim.Count("berries")==berries-1&&_sim.Count("bread")==2&&_slot==0,"food shortcut consumes its exact item and keeps equipment");
        _state.Player.Inventory["health_potion"]=2;Command("hotbar:assign:6:item:health_potion");
        _state.Player.Health=5;ActivateHotbar(6);float health=_state.Player.Health;ActivateHotbar(6);
        Check(_sim.Count("health_potion")==1&&_state.Player.Health==health,"potion cooldown blocks repeated use");
        _state.Player.Stamina=100;ActivateHotbar(7);float stamina=_state.Player.Stamina;
        Check(_dodge>0&&stamina==76,"skill shortcut starts dodge and charges once");
        _UnhandledInput(new InputEventKey{Keycode=Key.Space,Pressed=true});
        Check(_state.Player.Stamina==stamina,"Space and skill shortcut share cooldown");
        foreach(var pair in new[]{(Key.P,"character"),(Key.K,"skills"),(Key.J,"journal")})
        {
            _UnhandledInput(new InputEventKey{Keycode=pair.Item1,Pressed=true});
            Check(_hud.IsPlayerScreenOpen&&_hud.ActivePlayerScreen==pair.Item2&&!_cameraRig.Captured,"screen shortcut opens and releases camera");
            _UnhandledInput(new InputEventKey{Keycode=Key.Key0,Pressed=true});Check(_slot==0,"modal prevents hotbar gameplay activation");
            _UnhandledInput(new InputEventKey{Keycode=pair.Item1,Pressed=true});Check(!_hud.IsPanelOpen,"screen shortcut toggles closed");
        }
        Command("screen:inventory");Check(_hud.IsInventoryOpen,"navigation opens inventory");
        Command("screen:character");Check(!_hud.IsInventoryOpen&&_hud.IsPlayerScreenOpen,"navigation closes previous screen");
        _hud.HidePanel();ActivateHotbar(9);
        string layout=JsonSerializer.Serialize(_state.Player.Hotbar);
        SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));
        Check(layout==JsonSerializer.Serialize(_state.Player.Hotbar)&&_slot==1,"saved layout and equipment restored in engine");
        CreateWorld(previous);_hud.HidePanel();
        GD.Print("PLAYER_MENUS_TEST_PASS: reordered number keys and hammer gate, exact food, potion cooldown, shared dodge cost, screen navigation, modal input isolation and saved equipment/layout");
    }

    private Vector2 _dragStart;
    private void VerifyHotbarPointerInput()
    {
        try
        {
            void Check(bool ok,string message){if(!ok)throw new Exception(message);}
            Control Find(string name)=>_hud.FindChild(name,true,false) as Control??throw new Exception("Missing drag control "+name);
            void Motion(Vector2 at,Vector2 relative)=>GetViewport().PushInput(new InputEventMouseMotion{Position=at,GlobalPosition=at,Relative=relative,ButtonMask=MouseButtonMask.Left},true);
            void Button(Vector2 at,bool down)=>GetViewport().PushInput(new InputEventMouseButton{Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,Pressed=down},true);
            if(_frames==45)
            {
                _dragStart=Find("InventoryItem_basic_sword").GetGlobalRect().GetCenter();
                Motion(_dragStart,Vector2.Zero);Button(_dragStart,true);
            }
            if(_frames==46){Motion(_dragStart+new Vector2(22,0),new Vector2(22,0));}
            if(_frames==47)
            {
                Check(GetViewport().GuiIsDragging(),"inventory pointer movement must start native drag");
                var target=Find("HotbarEditor_8").GetGlobalRect().GetCenter();Motion(target,target-_dragStart);Button(target,false);
                Check(_state.Player.Hotbar[8]?.Id=="basic_sword"&&_sim.Count("basic_sword")==1,"drop binds sword without consuming it");
            }
            if(_frames==49)
            {
                _dragStart=Find("HotbarEditor_8").GetGlobalRect().GetCenter();Motion(_dragStart,Vector2.Zero);Button(_dragStart,true);
            }
            if(_frames==50)Motion(_dragStart+new Vector2(22,0),new Vector2(22,0));
            if(_frames==51)
            {
                Check(GetViewport().GuiIsDragging(),"hotbar pointer movement must start native drag");
                var target=Find("HotbarEditor_9").GetGlobalRect().GetCenter();Motion(target,target-_dragStart);Button(target,false);
                Check(_state.Player.Hotbar[9]?.Id=="basic_sword"&&_state.Player.Hotbar[8]==null,"drop moves hotbar binding to empty slot");
                _hud.HidePanel();_UnhandledInput(new InputEventKey{Keycode=Key.Key0,Pressed=true});Check(_slot==4,"reordered sword activates with 0");ShowPlayerScreen("skills");
            }
            if(_frames==54)
            {
                _dragStart=Find("SkillCard_dodge").GetGlobalRect().GetCenter();Motion(_dragStart,Vector2.Zero);Button(_dragStart,true);
            }
            if(_frames==55)Motion(_dragStart+new Vector2(22,0),new Vector2(22,0));
            if(_frames==56)
            {
                Check(GetViewport().GuiIsDragging(),"ability card must start native drag");
                var target=_hud.FindChildren("HotbarEditor_6","",true,false).OfType<Control>().First(c=>c.IsVisibleInTree()).GetGlobalRect().GetCenter();Motion(target,target-_dragStart);Button(target,false);
                Check(_state.Player.Hotbar[6]?.Kind=="skill"&&_state.Player.Hotbar[6]?.Id=="dodge","ability drop binds a skill");
            }
            if(_frames==58)
            {
                var nav=_hud.FindChildren("Navigate_character","",true,false).OfType<Control>().First(c=>c.IsVisibleInTree()).GetGlobalRect().GetCenter();Motion(nav,Vector2.Zero);Button(nav,true);Button(nav,false);
                Check(_hud.IsPlayerScreenOpen&&_hud.ActivePlayerScreen=="character","live menu navigation button opens character");
                ShowInventory();
                GD.Print("HOTBAR_POINTER_TEST_PASS: native item and skill drag/drop, hotbar reorder, inventory preservation, remapped 0 activation and menu navigation click");
            }
        }
        catch(Exception error){GD.PushError("HOTBAR_POINTER_TEST_FAIL "+error);QuitGame(2);}
    }
}
