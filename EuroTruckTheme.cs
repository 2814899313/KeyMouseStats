using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace KeyMouseStats {
internal static class EuroTruckArt {
 internal static bool Active { get { return Store.ThemeId==11; } }
 internal static readonly int[] NavSymbols={4,3,7,8,6,15};
 internal static readonly int[] MetricSymbols={8,9,10,2,7,4,3,14};
 internal static readonly string[] PageLabels={"DEPOT / START","ROUTE / TREND","DRIVE / 24H","CONTROL / INPUT","FREIGHT / APPS","JOURNEY / REPORT"};
 static readonly string[] Pages={"EuroTruckOverview","EuroTruckTrend","EuroTruckHours","EuroTruckKeys","EuroTruckApps","EuroTruckInsights"};
 static Image Icons { get{return NikkiArt.Load("EuroTruckIcons");} } static Image Widget {get{return NikkiArt.Load("EuroTruckWidget");}}
 internal static void DrawPage(Graphics g,int page,RectangleF bounds){if(!Active||page<0||page>=Pages.Length)return;Image image=NikkiArt.Load(Pages[page]);if(image==null)return;NikkiArt.DrawCached(g,image,WuxiaArt.Cover(new Rectangle(0,0,image.Width,image.Height),bounds),bounds,.86f,"eurotruck-page-"+page);}
 internal static void DrawWidget(Graphics g,RectangleF bounds){Image image=Widget;if(!Active||image==null)return;NikkiArt.DrawCached(g,image,WuxiaArt.Cover(new Rectangle(0,0,image.Width,image.Height),bounds),bounds,.96f,"eurotruck-widget");}
 internal static void Symbol(Graphics g,int kind,RectangleF bounds,float opacity=1f){Image atlas=Icons;if(!Active||atlas==null)return;kind=Math.Max(0,Math.Min(15,kind));int col=kind%4,row=kind/4;Rectangle source=Rectangle.FromLTRB(col*atlas.Width/4,row*atlas.Height/4,(col+1)*atlas.Width/4,(row+1)*atlas.Height/4);NikkiArt.DrawCached(g,atlas,source,bounds,opacity,"eurotruck-icon-"+kind);}
 internal static void CardOrnaments(Graphics g,RectangleF r){Color amber=ArtTheme.Current.Orange,cyan=ArtTheme.Current.Cyan;using(Pen road=new Pen(Color.FromArgb(175,amber),1.5f)){g.DrawLine(road,r.X+9,r.Y,r.X+45,r.Y);g.DrawLine(road,r.Right-30,r.Bottom,r.Right-9,r.Bottom);}using(Pen route=new Pen(Color.FromArgb(125,cyan),1f)){route.DashStyle=DashStyle.Dash;g.DrawLine(route,r.X+50,r.Y,r.X+Math.Min(r.Width-38,104),r.Y);}using(SolidBrush lamp=new SolidBrush(Color.FromArgb(210,amber))){g.FillEllipse(lamp,r.X+5,r.Y+5,3,3);g.FillEllipse(lamp,r.Right-8,r.Bottom-8,3,3);}}
 internal static void Button(Graphics g,RectangleF r,bool selected,bool hover){Color edge=selected?ArtTheme.Current.Orange:hover?ArtTheme.Current.Cyan:ArtTheme.Current.Line;using(GraphicsPath p=Dashboard.RoundedRect(r.X,r.Y,r.Width,r.Height,6))using(LinearGradientBrush fill=new LinearGradientBrush(r,selected?Color.FromArgb(235,74,52,22):Color.FromArgb(225,18,35,49),Color.FromArgb(230,7,17,28),90))using(Pen pen=new Pen(Color.FromArgb(selected||hover?235:165,edge),selected?1.8f:1f)){g.FillPath(fill,p);g.DrawPath(pen,p);}using(Pen rail=new Pen(Color.FromArgb(selected?235:110,edge),2))g.DrawLine(rail,r.X+8,r.Bottom-3,r.Right-8,r.Bottom-3);if(selected)using(SolidBrush lamp=new SolidBrush(ArtTheme.Current.Orange))g.FillEllipse(lamp,r.X+6,r.Y+5,4,4);}
 internal static void KeyCap(Graphics g,HeatKey key,RectangleF rect,bool champion){int icon=key.Label=="Space"?1:key.Label.IndexOf("Ctrl",StringComparison.OrdinalIgnoreCase)>=0?8:key.Label.Length==1&&char.IsDigit(key.Label[0])?4:10;float size=Math.Min(12,rect.Height*.4f);Symbol(g,icon,new RectangleF(rect.Right-size-2,rect.Y+2,size,size),champion?.92f:.38f);using(Pen p=new Pen(Color.FromArgb(champion?220:80,champion?ArtTheme.Current.Orange:ArtTheme.Current.Cyan),champion?1.4f:.7f))g.DrawRectangle(p,rect.X+2,rect.Y+2,Math.Max(1,rect.Width-4),Math.Max(1,rect.Height-4));}
}}
