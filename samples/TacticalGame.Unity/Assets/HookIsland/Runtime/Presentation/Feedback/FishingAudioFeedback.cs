using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Audio for confirmed outcomes. Poses and predicted state never call this directly.</summary>
    public sealed class FishingAudioFeedback : IDisposable
    {
        private readonly AudioSource audio;
        private readonly AudioClip caught,cast,hooked,comboStart,comboCap,sip,bite;
        private readonly List<AudioClip> clips=new List<AudioClip>();
        public int PresentedCatches {get;private set;}
        public int PresentedCasts {get;private set;}
        public int PresentedPlayerHooks {get;private set;}
        public FishingAudioFeedback(Transform root)
        {
            audio=root.gameObject.AddComponent<AudioSource>();audio.spatialBlend=0;audio.volume=.25f;
            sip=MealSound(true);bite=MealSound(false);
            caught=Tone("Catch chime",880,1320,.2f);cast=Tone("Hook cast",220,500,.09f);
            hooked=Tone("Teammate hooked",430,150,.19f);
            comboStart=Tone("Combo started",1046,1568,.16f);comboCap=Tone("Combo maximum",1318,2093,.25f);
        }
        public void Apply(HookGameplayEvent value,ushort local,ComboConfig combo,FryingPanArt melee)
        {
            switch(value.Kind)
            {
                case HookGameplayEventKind.FoodConsumed:
                    audio.PlayOneShot(value.ItemType==0?sip:bite,value.Slot==local?.65f:.3f);break;
                case HookGameplayEventKind.HookCast:
                    PresentedCasts++;if(value.Slot==local)audio.PlayOneShot(cast);break;
                case HookGameplayEventKind.FishCaught:
                    PresentedCatches++;audio.PlayOneShot(caught);break;
                case HookGameplayEventKind.PlayerHooked:
                    PresentedPlayerHooks++;if(value.Slot==local || value.TargetId==local)audio.PlayOneShot(hooked);break;
                case HookGameplayEventKind.Upgraded:
                    if(value.Slot==local)audio.PlayOneShot(caught);break;
                case HookGameplayEventKind.ComboIncreased:
                    if(value.Slot!=local || combo?.Enabled!=true)break;
                    if(value.Value==combo.GoldBonusPercent.Count)audio.PlayOneShot(comboCap,.7f);
                    else if(value.Value==combo.DisplayStartCount)audio.PlayOneShot(comboStart,.55f);
                    break;
                case HookGameplayEventKind.MeleeSwing:
                    if(melee!=null)audio.PlayOneShot(melee.Swing,.45f);break;
                case HookGameplayEventKind.MeleeHit:
                    if(melee!=null)audio.PlayOneShot(melee.Impact,.8f);break;
            }
        }
        private AudioClip MealSound(bool drinking)
        {
            const int rate=22050;var samples=new float[(int)(rate*.34f)];uint noise=157;float smooth=0;
            for(int n=0;n<samples.Length;n++)
            {
                float t=n/(float)rate;noise=unchecked(noise*1664525+1013904223);
                float raw=(noise>>8)/8388608f-1;smooth=Mathf.Lerp(smooth,raw,.12f);
                float envelope=Mathf.Exp(-Mathf.Pow((t-.07f)/.035f,2))+Mathf.Exp(-Mathf.Pow((t-.2f)/.045f,2))*.7f;
                samples[n]=envelope*(drinking?Mathf.Sin((t*165+t*t*240)*Mathf.PI*2)*.22f+smooth*.25f:(raw-smooth)*.18f+smooth*.3f);
            }
            var result=AudioClip.Create(drinking?"Coconut sip":"Food bite",samples.Length,1,rate,false);result.SetData(samples,0);clips.Add(result);return result;
        }
        private AudioClip Tone(string name,float start,float end,float duration)
        {
            var samples=new float[(int)(22050*duration)];float phase=0;
            for(int i=0;i<samples.Length;i++){float t=i/(float)samples.Length;phase+=Mathf.Lerp(start,end,t)/22050*Mathf.PI*2;samples[i]=Mathf.Sin(phase)*Mathf.Sin(t*Mathf.PI)*(1-t)*.45f;}
            var clip=AudioClip.Create(name,samples.Length,1,22050,false);clip.SetData(samples,0);clips.Add(clip);return clip;
        }
        public void Reset(){audio.Stop();PresentedCatches=PresentedCasts=PresentedPlayerHooks=0;}
        public void Dispose(){foreach(var clip in clips)Release(clip);Release(audio);}
        private static void Release(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }
}
