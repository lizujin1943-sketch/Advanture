using UnityEngine;

namespace Advanture.HookIsland
{
    internal static class FisherArmPose
    {
        internal static void Reach(Transform body,Transform upper,Transform forearm,Transform hand,Vector3 goal)
        {
            Vector3 shoulder=upper.position;float first=Vector3.Distance(shoulder,forearm.position),second=Vector3.Distance(forearm.position,hand.position);
            Vector3 delta=goal-shoulder;float distance=Mathf.Clamp(delta.magnitude,.04f,first+second-.004f);Vector3 direction=delta.normalized;goal=shoulder+direction*distance;
            Vector3 bend=Vector3.ProjectOnPlane(body.TransformDirection(new Vector3(-1,.1f,-.1f)),direction).normalized;
            float along=(first*first-second*second+distance*distance)/(2*distance);float height=Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            Vector3 elbow=shoulder+direction*along+bend*height;
            upper.rotation=Quaternion.FromToRotation(forearm.position-shoulder,elbow-shoulder)*upper.rotation;
            forearm.rotation=Quaternion.FromToRotation(hand.position-forearm.position,goal-forearm.position)*forearm.rotation;
        }
    }
}
