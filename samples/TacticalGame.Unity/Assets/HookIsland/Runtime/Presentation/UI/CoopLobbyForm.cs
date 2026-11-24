using System;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>合作场景共用的房间入口。地址和人数是本机选择，开局由各自的房间会话处理。</summary>
    public sealed class CoopLobbyForm
    {
        private string address="127.0.0.1", port;
        private int capacity;

        public CoopLobbyForm(int defaultPort,int defaultCapacity)
        {port=defaultPort.ToString();capacity=defaultCapacity;}

        public bool TryGetPort(out int number)=>int.TryParse(port,out number) && number>0 && number<=65535;

        public void Draw(Rect panel,string heading,string description,string rules,GUIStyle title,GUIStyle body,
            GUIStyle small,GUIStyle button,GUIStyle field,Action<int,int> host,Action<string,int> join,Action solo)
        {
            float x=panel.x,y=panel.y;
            HudPainter.Fill(panel,new Color(.025f,.10f,.14f,.91f));
            GUI.Label(new Rect(x+28,y+18,472,38),heading,title);
            GUI.Label(new Rect(x+28,y+61,472,32),description,small);
            GUI.Label(new Rect(x+28,y+106,104,30),"房主地址",body);
            address=GUI.TextField(new Rect(x+136,y+102,230,35),address,45,field);
            port=GUI.TextField(new Rect(x+380,y+102,120,35),port,5,field);
            GUI.Label(new Rect(x+28,y+156,104,30),"房间人数",body);
            for(int i=2;i<=4;i++)
            {
                var old=GUI.backgroundColor;
                GUI.backgroundColor=capacity==i?new Color(.2f,.75f,.7f):Color.white;
                if(GUI.Button(new Rect(x+136+(i-2)*123,y+151,112,36),i+" 人",button))capacity=i;
                GUI.backgroundColor=old;
            }
            bool enabled=GUI.enabled;
            bool valid=TryGetPort(out int number);
            GUI.enabled=enabled && valid;
            if(GUI.Button(new Rect(x+28,y+207,224,46),"创建联机房间",button))host(number,capacity);
            if(GUI.Button(new Rect(x+276,y+207,224,46),"加入好友",button))join(address,number);
            GUI.enabled=enabled;
            if(GUI.Button(new Rect(x+28,y+267,224,42),"先自己玩",button))solo();
            GUI.Label(new Rect(x+276,y+268,224,57),"同机填 127.0.0.1\n不同电脑填房主局域网 IP",small);
            GUI.Label(new Rect(x+28,y+322,472,30),rules,small);
        }
    }
}
