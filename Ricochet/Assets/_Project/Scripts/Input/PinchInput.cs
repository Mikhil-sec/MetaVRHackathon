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
        /// <summary>Multiplier on the sling's grab radius. Infinity grabs from anywhere (mouse).</summary>
        float ReachScale { get; }
    }

    /// <summary>Pinch from an ISDK hand. The pinch point is the midpoint of the thumb and index tips.</summary>
    public sealed class HandPinchInput : IPinchInput
    {
        readonly IHand _hand;

        public HandPinchInput(IHand hand) => _hand = hand;

        public bool IsTracked => _hand != null && _hand.IsConnected && _hand.IsTrackedDataValid;
        public bool IsPinching => IsTracked && _hand.GetIndexFingerIsPinching();
        public float ReachScale => 1f;

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
