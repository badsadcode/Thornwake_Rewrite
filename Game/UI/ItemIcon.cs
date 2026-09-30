using Godot;

namespace Thornwake.UI;

/// <summary>Small original illustrations drawn at UI resolution; no external fonts or assets.</summary>
public partial class ItemIcon : Control
{
    public string Item { get; set; } = "wood";
    public Color Accent { get; set; } = new("cbb57b");
    public ItemIcon() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(52, 52); }
    public override void _Draw()
    {
        float scale = Mathf.Min(Size.X, Size.Y) / 64;
        DrawSetTransform((Size - new Vector2(64,64)*scale)*.5f, 0, new Vector2(scale,scale));
        Color dark = new("343d32"), wood = new("b08c60"), light = new("d9cba8"), green = new("91a979"), stone = new("abb5ac");
        void Line(Vector2 a,Vector2 b,Color c,float width=3)=>DrawLine(a,b,c,width,true);
        void Rect(float x,float y,float w,float h,Color c)=>DrawRect(new Rect2(x,y,w,h),c);
        void Poly(Color c,params Vector2[] points)=>DrawColoredPolygon(points,c);
        void Circle(float x,float y,float r,Color c)=>DrawCircle(new Vector2(x,y),r,c);
        string id = Item.ToLowerInvariant();
        if(id=="shovel")
        {
            Line(new(41,11),new(23,42),wood,5);Line(new(35,8),new(47,15),wood,4);
            Poly(wood,new(16,34),new(32,44),new(28,56),new(16,60),new(10,50));
            Line(new(11,51),new(17,57),stone,3);Line(new(17,57),new(27,54),stone,3);
        }
        else if(id=="soil")
        {
            Poly(new Color("806145"),new(6,51),new(19,33),new(28,36),new(39,24),new(55,47),new(58,55),new(14,58));
            Rect(24,43,7,5,wood);Rect(38,37,5,4,wood);Rect(16,49,4,3,dark);Rect(42,49,7,4,dark);
        }
        else if(id=="pine_cone")
        {
            Line(new(34,11),new(32,19),wood,3);
            Poly(new Color("795b3e"),new(32,13),new(44,23),new(49,40),new(41,53),new(28,58),new(17,46),new(19,28));
            for(int row=0;row<4;row++)for(int col=0;col<3;col++)
            {
                float x=24+col*8+(row%2==0?0:-3),y=24+row*8;
                Poly(row%2==0?wood:new Color("c3a072"),new(x-4,y-3),new(x+4,y-3),new(x+3,y+2),new(x,y+5),new(x-3,y+2));
            }
        }
        else if(id=="pine_sapling")
        {
            Poly(new Color("6b5941"),new(20,50),new(41,50),new(45,57),new(17,57));
            Line(new(32,53),new(32,16),wood,3);
            Poly(new Color("678761"),new(32,9),new(19,28),new(26,27),new(15,40),new(27,37),new(20,46),new(44,46),new(38,37),new(49,40),new(39,27),new(45,28));
            Line(new(32,19),new(32,42),new Color("a0b184"),1.5f);
            Line(new(23,33),new(30,35),green,2);Line(new(35,28),new(41,31),green,2);
        }
        else if(id=="bush_seedling")
        {
            Poly(new Color("6b5941"),new(20,51),new(43,51),new(47,57),new(16,57));
            Line(new(32,53),new(31,22),wood,3);Line(new(32,43),new(19,31),wood,2);Line(new(31,39),new(45,27),wood,2);
            Poly(green,new(31,26),new(26,11),new(36,15),new(37,23));
            Poly(new Color("769568"),new(23,36),new(10,25),new(23,24),new(28,32));
            Poly(green,new(38,34),new(42,18),new(53,17),new(49,29));
            Poly(new Color("9bb37c"),new(29,43),new(14,38),new(21,33),new(29,36));
            Circle(39,37,3,new Color("a67585"));Circle(44,35,3,new Color("b38798"));
        }
        else if(id=="dodge")
        {
            Circle(37,16,6,light);Line(new(35,24),new(28,37),light,5);Line(new(30,30),new(45,34),light,4);
            Line(new(28,37),new(17,51),light,4);Line(new(28,37),new(43,46),light,4);
            Line(new(10,22),new(24,22),green,3);Line(new(8,31),new(20,31),green,3);
            Poly(Accent,new(48,41),new(58,47),new(46,53));Line(new(38,48),new(52,48),Accent,3);
        }
        else if(id.Contains("potion"))
        {
            Rect(25,8,14,7,wood);Rect(27,15,10,13,stone);
            Poly(stone,new(27,24),new(37,24),new(48,38),new(47,51),new(41,57),new(23,57),new(17,51),new(16,38));
            Poly(new Color("a46361"),new(21,36),new(43,36),new(43,49),new(38,53),new(26,53),new(21,48));
            Line(new(24,32),new(22,41),light,3);Rect(28,39,8,10,light);
        }
        else if(id.Contains("lantern"))
        {
            DrawArc(new(32,14),7,Mathf.Pi,Mathf.Tau,16,stone,3,true);
            Poly(stone,new(20,20),new(25,13),new(39,13),new(44,20));
            Rect(22,22,20,27,new Color("806943"));Rect(25,24,14,24,new Color("c7aa66"));
            Poly(light,new(29,44),new(29,34),new(34,29),new(36,43),new(33,47));
            Line(new(20,20),new(23,51),stone,3);Line(new(44,20),new(41,51),stone,3);Rect(19,50,26,5,stone);
        }
        else if(id=="hammer")
        {
            Line(new(23,55),new(35,23),wood,6);Poly(stone,new(17,17),new(24,9),new(48,18),new(49,28),new(42,33),new(18,25));
            Line(new(23,12),new(23,25),light,3);Line(new(43,21),new(43,30),dark,2);
        }
        else if(id=="hoe")
        {
            Line(new(19,56),new(40,12),wood,5);Poly(stone,new(35,13),new(45,17),new(52,35),new(42,39),new(40,23),new(32,21));
            Line(new(43,35),new(50,32),light,2);
        }
        else if(id.Contains("iron")&&!id.Contains("ore"))
        {
            for(int i=0;i<2;i++)
            {float y=i*17;Poly(stone,new(11,22+y),new(38,11+y),new(53,19+y),new(26,31+y));Poly(dark,new(11,22+y),new(26,31+y),new(53,19+y),new(52,27+y),new(26,39+y),new(12,31+y));Line(new(14,23+y),new(26,29+y),light,2);}
        }
        else if(id.Contains("brick"))
        {
            var clay=new Color("b27d64");var edge=new Color("805c4d");
            for(int i=0;i<2;i++)
            {float y=i*18;Poly(clay,new(9,25+y),new(34,12+y),new(56,23+y),new(31,36+y));Poly(edge,new(9,25+y),new(31,36+y),new(56,23+y),new(56,32+y),new(31,45+y),new(9,34+y));Line(new(12,25+y),new(31,34+y),light,1.5f);}
        }
        else if(id.Contains("clay"))
        {
            Color clay=new("b28a70");Circle(31,34,18,clay);Circle(18,42,12,clay);Circle(45,41,11,clay);
            DrawArc(new(30,31),11,3.5f,5.8f,16,new Color("d7b69a"),3,true);Line(new(17,47),new(43,49),new Color("815f50"),2);
        }
        else if(id.Contains("rug"))
        {
            Poly(new Color("a77268"),new(9,18),new(50,11),new(56,47),new(15,54));
            Poly(wood,new(15,22),new(46,17),new(50,43),new(20,48));
            Poly(new Color("b98779"),new(20,26),new(42,22),new(45,40),new(24,43));
            Poly(light,new(27,32),new(33,27),new(40,34),new(34,40));
            for(int i=0;i<7;i++){Line(new(10+i*6,18-i),new(9+i*6,12-i),light,1.5f);Line(new(15+i*6,54-i),new(16+i*6,59-i),light,1.5f);}
        }
        else if(id.Contains("bread"))
        {
            Poly(new Color("b18a51"),new(10,41),new(12,28),new(20,18),new(37,15),new(49,23),new(54,38),new(49,48),new(20,52));
            DrawArc(new(31,31),18,3.4f,5.7f,18,new Color("d7b879"),8,true);
            for(int i=0;i<3;i++)Line(new(22+i*10,24+i),new(18+i*10,34+i),light,3);
        }
        else if(id.Contains("triangle"))
        {
            bool roof=id.Contains("roof");Color surface=roof?green:wood;
            Poly(surface,new(8,42),new(31,14),new(58,42));
            Poly(dark,new(8,42),new(58,42),new(55,48),new(13,48));
            Line(new(8,42),new(31,14),light,2);Line(new(31,14),new(58,42),light,2);
            for(int i=0;i<3;i++)Line(new(18+i*4,33-i*5),new(47-i*4,33-i*5),roof?dark:light,1.5f);
        }
        else if(id=="wall_diagonal")
        {
            Poly(light,new(12,12),new(53,24),new(53,57),new(12,45));
            Line(new(12,12),new(12,45),wood,5);Line(new(53,24),new(53,57),wood,5);Line(new(12,12),new(53,24),wood,5);
            Line(new(15,42),new(50,27),wood,4);
        }
        else if(id.Contains("roof")&&(id.Contains("slope")||id.Contains("eave")))
        {
            bool eave=id.Contains("eave");
            Poly(green,new(7,eave?30:44),new(28,13),new(58,25),new(36,eave?40:56));
            Line(new(7,eave?30:44),new(36,eave?40:56),wood,4);
            for(int i=1;i<4;i++)Line(new(7+i*5,eave?30-i*4:44-i*7),new(36+i*5,eave?40-i*4:56-i*7),light,1.5f);
        }
        else if(id.Contains("roof")&&id.Contains("gable_edge"))
        {
            Poly(light,new(8,48),new(32,13),new(57,48));
            Line(new(8,48),new(32,13),wood,4);Line(new(32,13),new(57,48),wood,4);Line(new(32,14),new(32,48),wood,3);
        }
        else if(id.Contains("roof"))
        {
            Poly(green,new(5,34),new(30,13),new(58,34),new(49,43),new(30,29),new(14,43));
            for(int i=0;i<3;i++)Line(new(13+i*6,34-i*5),new(30,21+i*4),dark,1.5f);
            Line(new(30,13),new(58,34),light,2);
        }
        else if(id.Contains("stairs"))
        {
            for(int i=0;i<4;i++){Rect(8+i*12,46-i*10,12,10+i*10,wood);Line(new(8+i*12,46-i*10),new(20+i*12,46-i*10),light,2);}
        }
        else if(id.Contains("foundation")||id=="floor")
        {
            Poly(wood,new(5,33),new(30,18),new(59,32),new(34,48));
            Poly(dark,new(5,33),new(34,48),new(59,32),new(59,40),new(34,56),new(5,41));
            for(int i=1;i<5;i++)Line(new(5+i*5,33-i*3),new(34+i*5,48-i*3),light,1);
        }
        else if(id.Contains("painting"))
        {
            Rect(9,10,46,43,wood);Rect(14,15,36,33,new Color("859f91"));Circle(39,24,5,light);
            Poly(green,new(14,44),new(26,28),new(40,45));Poly(dark,new(26,47),new(42,29),new(50,39),new(50,47));
        }
        else if(id.Contains("plant")||id.Contains("flower"))
        {
            Line(new(32,43),new(32,13),green,3);Poly(green,new(31,29),new(13,18),new(15,31),new(31,36));
            Poly(green,new(33,24),new(51,12),new(48,26),new(33,32));Poly(wood,new(19,39),new(46,39),new(42,58),new(23,58));
            Line(new(18,39),new(46,39),light,4);
        }
        else if(id.Contains("chair"))
        {
            Rect(15,9,8,31,wood);Rect(16,12,32,7,wood);Rect(42,9,7,31,wood);Rect(13,35,38,7,light);
            Line(new(18,42),new(16,58),wood,5);Line(new(46,42),new(48,58),wood,5);
        }
        else if(id.Contains("table")||id.Contains("bench")||id.Contains("carpenter"))
        {
            Poly(wood,new(7,26),new(35,16),new(59,28),new(29,38));Line(new(12,31),new(12,53),light,5);
            Line(new(52,32),new(52,54),light,5);Line(new(29,39),new(29,59),wood,5);
        }
        else if(id.Contains("wall")||id.Contains("door")||id.Contains("fence"))
        {
            bool door=id.Contains("door");Rect(10,10,44,45,door?wood:light);Rect(10,10,5,45,wood);Rect(49,10,5,45,wood);
            for(int i=0;i<3;i++)Line(new(19+i*10,15),new(19+i*10,52),dark,1);
            if(door){Rect(19,16,27,39,new Color("665841"));Circle(39,35,2,Accent);}else Line(new(16,49),new(48,15),wood,5);
        }
        else if(id.Contains("axe")||id.Contains("sword")||id.Contains("blade")||id=="hammer"||id=="hoe")
        {
            Line(new(18,54),new(42,13),wood,6);
            if(id.Contains("sword")||id.Contains("blade")){Poly(stone,new(27,36),new(43,8),new(49,6),new(49,16),new(33,40));Line(new(22,35),new(39,45),light,4);}
            else {Poly(stone,new(32,12),new(51,11),new(56,20),new(44,28),new(35,24));Line(new(33,14),new(45,24),dark,2);}
        }
        else if(id.Contains("berries")||id.Contains("seed"))
        {
            bool seeds=id.Contains("seed");for(int i=0;i<6;i++){float x=21+i%3*11,y=29+i/3*12;Circle(x,y,seeds?4:7,seeds?wood:new Color("aa7283"));Circle(x-2,y-2,1.5f,light);}
            Line(new(32,27),new(34,13),green,3);Poly(green,new(33,20),new(45,14),new(44,23));
        }
        else if(id.Contains("meal")||id.Contains("bread"))
        {
            DrawArc(new Vector2(32,32),21,0,Mathf.Pi,24,light,9,true);Rect(11,30,42,7,light);
            for(int i=0;i<3;i++)Circle(22+i*10,28,7,i==1?green:wood);
        }
        else if(id.Contains("fire")||id.Contains("kiln")||id.Contains("oven")||id.Contains("forge")||id.Contains("cooking"))
        {
            if(!id.Contains("fire")){Rect(11,19,42,37,stone);Rect(18,29,28,24,dark);Rect(40,9,10,17,stone);}
            else{Line(new(12,52),new(51,46),wood,6);Line(new(13,46),new(50,54),wood,6);}
            Poly(new Color("d4a663"),new(19,43),new(22,29),new(31,35),new(36,12),new(45,38),new(39,49),new(26,49));
            Poly(light,new(28,43),new(33,29),new(38,44),new(33,49));
        }
        else if(id.Contains("coin"))
        {
            for(int i=0;i<3;i++){Circle(23+i*9,38-i*7,12,Accent);DrawArc(new(23+i*9,38-i*7),9,0,Mathf.Tau,24,wood,1.5f,true);}
        }
        else if(id.Contains("stone")||id.Contains("clay")||id.Contains("ore")||id.Contains("brick"))
        {
            Poly(stone,new(9,41),new(19,22),new(38,17),new(54,31),new(49,48),new(27,54));
            Poly(light,new(19,22),new(38,17),new(33,35),new(9,41));Line(new(33,35),new(49,48),dark,2);
        }
        else if(id.Contains("plank"))
        {
            for(int i=0;i<3;i++){Poly(i==1?light:wood,new(9,20+i*10),new(49,12+i*10),new(55,21+i*10),new(15,29+i*10));Line(new(18,23+i*10),new(43,18+i*10),dark,1);}
        }
        else if(id.Contains("wood")||id.Contains("timber"))
        {
            Line(new(20,40),new(46,19),wood,20);Circle(20,40,10,light);DrawArc(new(20,40),6,0,Mathf.Tau,20,wood,2,true);Line(new(24,45),new(51,24),dark,2);
        }
        else if(id.Contains("hide")||id.Contains("rug"))
        {
            Poly(wood,new(13,13),new(28,19),new(45,12),new(50,28),new(43,38),new(50,55),new(30,48),new(14,55),new(19,37),new(10,26));
            Line(new(25,25),new(38,40),light,2);
        }
        else
        {
            Poly(wood,new(18,18),new(45,18),new(52,51),new(12,51));Line(new(18,20),new(45,20),light,4);Line(new(27,21),new(30,39),light,2);
        }
    }
}
