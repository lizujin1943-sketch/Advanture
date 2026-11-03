using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Lazy per-avatar model instances; switching back reuses geometry and moving parts.</summary>
    public sealed class FirearmModelSelection
    {
        private readonly Transform parent;
        private readonly FirearmArt art;
        private readonly Dictionary<string,FirearmArt.Model> models=new Dictionary<string,FirearmArt.Model>();
        private FirearmArt.Model current;
        private string currentId;
        public FirearmModelSelection(Transform parent,FirearmArt art){this.parent=parent;this.art=art;}
        public FirearmArt.Model Select(FirearmDefinition gun,bool visible=true)
        {
            if(gun?.Id==currentId && current!=null){current.Root.gameObject.SetActive(gun!=null && visible);return current;}
            if(current!=null)current.Root.gameObject.SetActive(false);
            currentId=gun?.Id;if(gun==null)return current;
            if(!models.TryGetValue(gun.Id,out current)){current=art.Create(parent,gun);models.Add(gun.Id,current);}
            current.Root.gameObject.SetActive(visible);return current;
        }
    }
}
