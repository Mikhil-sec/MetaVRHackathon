using Oculus.Interaction.Input;
using UnityEngine;

namespace Ricochet.Input
{
    /// <summary>One source of pinch gestures (a tracked hand, or the mouse on desktop).</summary>
    public interface IPinchInput
    {
        bool IsTracked { get; }
        bool IsPinching { get; }
        Vector3 PinchPoint { get; }
        /// <summary>True for sources that grab from anywhere (mouse), false for spatial sources that must reach the Spark.</summary>
        bool GrabsFromAnywhere { get; }
    }

    /// <summary>Pinch from an ISDK hand. The pinch point is the midpoint of the thumb and index tips.</summary>
    public sealed class HandPinchInput : IPinchInput
    {
        readonly IHand _hand;

        public HandPinchInput(IHand hand) => _hand = hand;

        public bool IsTracked => _hand != null && _hand.IsConnected && _hand.IsTrackedDataValid;
        public bool IsPinching => IsTracked && _hand.GetIndexFingerIsPinching();
        public bool GrabsFromAnywhere => false;

        public Vector3 PinchPoint
        {
            get
            {
                if (_hand.GetJointPose(HandJointId.HandThumbTip, out var thumb) &&
                    _hand.GetJointPose(HandJointId.HandIndexTip, out var index))
                {
                    return (thumb.position + index.position) * 0.5f;
                }
                return Vector3.zero;
            }
        }
    }
}
