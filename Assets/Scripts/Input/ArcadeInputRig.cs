using ChunithmMobile.Platform;
using UnityEngine;

namespace ChunithmMobile.Input
{
    // 输入总线：统一收集地面触控与 AIR 传感器，并以同一音频单调时钟输出。
    // 判定器读取本类的快照，UI 反馈可以读取同一快照，但反馈不能反向修改输入。
    [DefaultExecutionOrder(-1000)]
    public sealed class ArcadeInputRig : MonoBehaviour
    {
        public ArcadeTouchInput Ground { get; private set; }
        public AirSensorInput Air { get; private set; }
        public bool InputSuppressed { get; private set; }

        private void Awake()
        {
            Ground = gameObject.AddComponent<ArcadeTouchInput>();
            Air = gameObject.AddComponent<AirSensorInput>();
            Air.Bind(Ground.Current);
        }

        private void Update()
        {
            if (InputSuppressed) {
                // Pause menus must not leave a touch or AIR motion sample
                // alive for the judge or the visual feedback layers.
                Ground?.ResetCaptureState();
                Air?.ResetCaptureState();
                return;
            }
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

        public void SetInputSuppressed(bool suppressed)
        {
            InputSuppressed = suppressed;
            if (suppressed) ResetCaptureState();
        }
    }
}
