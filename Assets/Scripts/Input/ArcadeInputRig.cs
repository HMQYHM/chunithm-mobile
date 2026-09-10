using ChunithmMobile.Platform;
using UnityEngine;

namespace ChunithmMobile.Input
{
    [DefaultExecutionOrder(-1000)]
    public sealed class ArcadeInputRig : MonoBehaviour
    {
        public ArcadeTouchInput Ground { get; private set; }
        public AirSensorInput Air { get; private set; }

        private void Awake()
        {
            Ground = gameObject.AddComponent<ArcadeTouchInput>();
            Air = gameObject.AddComponent<AirSensorInput>();
            Air.Bind(Ground.Current);
        }

        private void Update()
        {
            // Capture Ground and AIR from one DSP timestamp before the judge
            // controller runs. This removes the one-frame disagreement that
            // occurred when the two input components sampled independently.
            double now = PlatformServices.Current.AudioTime.Seconds * 1000.0;
            Ground.Capture(now);
            Air.Capture(now);
        }

        public void ResetCaptureState()
        {
            Ground?.ResetCaptureState();
            Air?.ResetCaptureState();
        }
    }
}
