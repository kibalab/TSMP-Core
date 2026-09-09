using UnityEngine;
using UnityEngine.Playables;

#if UDONSHARP || COMPILER_UDONSHARP
using UdonSharp;
#endif

namespace K13A.TSMP.Udon
{
    public class TSMPNetworkTimelineSync : TSMPNetworkBehaviour
    {
        public PlayableDirector director;
        public float timeApplyThreshold = 0.05f;
        [HideInInspector] public int encodedTimelineBytes;

        [HideInInspector]
        [TransSync("timeline.packed")]
#if UDONSHARP || COMPILER_UDONSHARP
        [FieldChangeCallback(nameof(TimelineBytes))]
#endif
        public byte[] timelineBytes;

        private const byte PacketVersion = 1;
        private const int PacketBytes = 10;
        private const byte StateStopped = 0;
        private const byte StatePaused = 1;
        private const byte StatePlaying = 2;
        private double _lastEncodedTime = -1.0;

        public byte[] TimelineBytes
        {
            get => timelineBytes;
            set
            {
                timelineBytes = value;
            }
        }

        private void Start()
        {
            ResolveDirector();
        }

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            ResolveDirector();
            if (director == null)
                return;

            if (timelineBytes == null || timelineBytes.Length != PacketBytes)
                timelineBytes = new byte[PacketBytes];

            timelineBytes[0] = PacketVersion;
            timelineBytes[1] = GetDirectorState();
            Binary.WriteFloat32LE(timelineBytes, 2, (float)director.time);
            Binary.WriteFloat32LE(timelineBytes, 6, (float)director.duration);
            encodedTimelineBytes = PacketBytes;
            _lastEncodedTime = director.time;
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
                return;

            ApplyTimelineBytes();
            OnTSMPVariableChanged(lastVariableHash);
        }

        private void ApplyTimelineBytes()
        {
            if (!IsTSMPActive() || timelineBytes == null || timelineBytes.Length < PacketBytes || timelineBytes[0] != PacketVersion)
                return;

            ResolveDirector();
            if (director == null)
                return;

            byte state = timelineBytes[1];
            float time = Binary.ReadFloat32LE(timelineBytes, 2);
            float currentTime = (float)director.time;
            if (Mathf.Abs(currentTime - time) > timeApplyThreshold || state != StatePlaying)
            {
                director.time = time;
                director.Evaluate();
            }

            if (state == StatePlaying)
            {
                director.Play();
            }
            else if (state == StatePaused)
            {
                director.Pause();
            }
            else
            {
                director.Stop();
            }
        }

        private void ResolveDirector()
        {
            if (director == null)
                director = GetComponent<PlayableDirector>();
        }

        private byte GetDirectorState()
        {
            double currentTime = director.time;
            if (_lastEncodedTime >= 0.0 && Mathf.Abs((float)(currentTime - _lastEncodedTime)) > 0.0001f)
                return StatePlaying;

            if (currentTime > 0.0 && currentTime < director.duration)
                return StatePaused;

            return StateStopped;
        }

    }
}
