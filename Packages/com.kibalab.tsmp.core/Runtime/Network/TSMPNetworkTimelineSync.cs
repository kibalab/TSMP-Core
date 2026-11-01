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
        [Min(0f)] public float timeApplyThreshold = 0.05f;
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
        private PlayableDirector _resolvedDirector;
        private byte _directorState;
        private bool _hasReceivedState;
        private byte _receivedState;
        private double _targetTime;
        private ReceiveInterpolationMode _receivedMode;
        private double _lastDspTime;
#if !COMPILER_UDONSHARP
        private PlayableAsset _resolvedAsset;
#endif

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

        private void OnEnable()
        {
            ResetTSMPLogBudget(16);
            _lastDspTime = AudioSettings.dspTime;
            _hasReceivedState = false;
        }

        private void OnDisable()
        {
            _hasReceivedState = false;
        }

        private void Update()
        {
            ResolveDirector();
            double dspTime = AudioSettings.dspTime;
            float delta = Time.deltaTime;
            if (director != null && director.timeUpdateMode == DirectorUpdateMode.UnscaledGameTime)
                delta = Time.unscaledDeltaTime;
            else if (director != null && director.timeUpdateMode == DirectorUpdateMode.DSPClock)
                delta = (float)(dspTime - _lastDspTime);
            _lastDspTime = dspTime;
            TickTimeline(delta);
        }

        public override void TSMPBeforeEncode()
        {
            if (!IsTSMPActive())
                return;

            ResolveDirector();
            if (!CanUseDirector())
            {
                ClearCapture();
                return;
            }

            float time = (float)director.time;
            float duration = (float)director.duration;
            if (!IsValidTime(time, duration))
            {
                ClearCapture();
                LogTSMPWarning("[TSMP Timeline] ", "Director time or duration cannot be encoded.", true, 1f);
                return;
            }

            if (timelineBytes == null || timelineBytes.Length != PacketBytes)
                timelineBytes = new byte[PacketBytes];

            timelineBytes[0] = PacketVersion;
            timelineBytes[1] = GetDirectorState();
            Binary.WriteFloat32LE(timelineBytes, 2, time);
            Binary.WriteFloat32LE(timelineBytes, 6, duration);
            encodedTimelineBytes = PacketBytes;
        }

        public override void OnTSMPVariableReceived()
        {
            if (receiveInterpolation == ReceiveInterpolationMode.None)
            {
                _hasReceivedState = false;
                return;
            }

            if (ApplyTimelineBytes())
                OnTSMPVariableChanged(lastVariableHash);
        }

        private bool ApplyTimelineBytes()
        {
            if (!IsTSMPActive())
                return false;
            if (timelineBytes == null || timelineBytes.Length == 0)
            {
                _hasReceivedState = false;
                return false;
            }
            if (timelineBytes.Length != PacketBytes || timelineBytes[0] != PacketVersion)
                return RejectPacket();

            byte state = timelineBytes[1];
            float time = Binary.ReadFloat32LE(timelineBytes, 2);
            float duration = Binary.ReadFloat32LE(timelineBytes, 6);
            if (state > StatePlaying || !IsValidTime(time, duration))
                return RejectPacket();

            ResolveDirector();
            if (!CanUseDirector())
                return false;

            byte currentState = GetDirectorState();
            bool transition = !_hasReceivedState || state != _receivedState || state != currentState || _receivedMode != receiveInterpolation;
            _targetTime = NormalizeTime(time);
            _receivedState = state;
            _receivedMode = receiveInterpolation;
            _hasReceivedState = true;

            if (state == StateStopped)
            {
                if (currentState != StateStopped)
                    director.Stop();
                _directorState = StateStopped;
                return true;
            }

            if (state == StatePaused)
            {
                if (currentState == StatePlaying)
                    director.Pause();
                ApplyTime(_targetTime, transition);
                _directorState = StatePaused;
                return true;
            }

            if (currentState == StateStopped)
                director.Play();
            else if (currentState == StatePaused)
                director.Resume();
            _directorState = StatePlaying;

            if (transition)
                ApplyTime(_targetTime, transition);
            else if (receiveInterpolation != ReceiveInterpolationMode.Continuous && NeedsCorrection(_targetTime))
                ApplyTime(_targetTime, false);
            return true;
        }

        private void TickTimeline(float delta)
        {
            if (!IsTSMPActive() || !CanUseDirector() || receiveInterpolation != _receivedMode)
            {
                _hasReceivedState = false;
                return;
            }
            if (!_hasReceivedState || receiveInterpolation != ReceiveInterpolationMode.Continuous || _receivedState != StatePlaying)
                return;
            if (GetDirectorState() != StatePlaying)
            {
                _hasReceivedState = false;
                return;
            }
            if (!(delta > 0f && delta <= float.MaxValue))
                return;

            if (director.timeUpdateMode != DirectorUpdateMode.Manual)
                _targetTime = NormalizeTime(_targetTime + delta);
            if (!NeedsCorrection(_targetTime))
                return;

            float rate = continuousInterpolationRate;
            float step = rate > 0f && rate <= float.MaxValue ? Mathf.Clamp01(rate * delta) : 1f;
            double next = NormalizeTime(director.time + GetTimeOffset(_targetTime) * step);
            ApplyTime(next, false);
        }

        private bool IsValidTime(float time, float duration)
        {
            return time >= 0f && time <= float.MaxValue && duration >= 0f && duration <= float.MaxValue && time <= duration;
        }

        private bool RejectPacket()
        {
            LogTSMPWarning("[TSMP Timeline] ", "Invalid timeline packet discarded.", true, 1f);
            return false;
        }

        private void ClearCapture()
        {
            timelineBytes = null;
            encodedTimelineBytes = 0;
        }

        private bool CanUseDirector()
        {
            if (director == null || !director.enabled || !director.gameObject.activeInHierarchy)
                return false;
#if !COMPILER_UDONSHARP
            if (director.playableAsset == null)
                return false;
#endif
            return true;
        }

        private double NormalizeTime(double time)
        {
            double duration = director.duration;
            if (!(duration > 0d && duration < double.MaxValue))
                return 0d;
            if (director.extrapolationMode == DirectorWrapMode.Loop)
            {
                time %= duration;
                return time < 0d ? time + duration : time;
            }
            return time < 0d ? 0d : time > duration ? duration : time;
        }

        private double GetTimeOffset(double time)
        {
            double offset = time - director.time;
            double duration = director.duration;
            if (director.extrapolationMode != DirectorWrapMode.Loop || !(duration > 0d && duration < double.MaxValue))
                return offset;
            offset %= duration;
            if (offset > duration * 0.5d)
                offset -= duration;
            else if (offset < -duration * 0.5d)
                offset += duration;
            return offset;
        }

        private bool NeedsCorrection(double time)
        {
            float threshold = timeApplyThreshold;
            if (!(threshold >= 0f && threshold <= float.MaxValue))
                threshold = 0f;
            double offset = GetTimeOffset(time);
            return offset > threshold || offset < -threshold;
        }

        private void ApplyTime(double time, bool force)
        {
            if (!force && director.time == time)
                return;
            director.time = time;
            director.Evaluate();
        }

        private void ResolveDirector()
        {
            if (director == null)
                director = GetComponent<PlayableDirector>();
            bool changed = _resolvedDirector != director;
#if !COMPILER_UDONSHARP
            PlayableAsset asset = director != null ? director.playableAsset : null;
            changed = changed || _resolvedAsset != asset;
            _resolvedAsset = asset;
#endif
            if (!changed)
                return;

            _hasReceivedState = false;
            _lastDspTime = AudioSettings.dspTime;
            _resolvedDirector = director;
            _directorState = director != null && director.playOnAwake ? StatePlaying : StateStopped;
#if !COMPILER_UDONSHARP
            if (!Application.isPlaying)
                _directorState = StateStopped;
#endif
        }

        private byte GetDirectorState()
        {
#if !COMPILER_UDONSHARP
            if (director.state == PlayState.Playing)
                return StatePlaying;
            return director.playableGraph.IsValid() ? StatePaused : StateStopped;
#else
            return _directorState;
#endif
        }

        public void Play()
        {
            ResolveDirector();
            _hasReceivedState = false;
            if (!CanUseDirector())
                return;
            director.Play();
            _directorState = StatePlaying;
        }

        public void Pause()
        {
            ResolveDirector();
            _hasReceivedState = false;
            if (!CanUseDirector())
                return;
            byte state = GetDirectorState();
            if (state != StatePlaying)
                return;
            director.Pause();
            _directorState = StatePaused;
        }

        public void Stop()
        {
            ResolveDirector();
            _hasReceivedState = false;
            if (director == null)
                return;
            if (GetDirectorState() != StateStopped)
                director.Stop();
            _directorState = StateStopped;
        }

        public void Resume()
        {
            Play();
        }

        public void Seek(float time)
        {
            ResolveDirector();
            if (!CanUseDirector() || !(time >= 0f && time <= float.MaxValue))
                return;
            _hasReceivedState = false;
            byte state = GetDirectorState();
            if (state == StateStopped)
                _directorState = StatePaused;
            ApplyTime(NormalizeTime(time), true);
        }

    }
}
