namespace EntBossHP
{
    internal enum RuntimeAutoSegmentDecisionStatus
    {
        None,
        Learned,
        Ambiguous
    }

    internal sealed record RuntimeAutoSegmentMark(bool Started, string MainCounter, int AttemptId);

    internal sealed record RuntimeAutoSegmentObservation(bool SuppressAutoCreate);

    internal sealed record RuntimeAutoSegmentDecision(
        RuntimeAutoSegmentDecisionStatus Status,
        string? MainCounter,
        string? SegmentCounter,
        int Mode,
        string Reason);

    internal sealed class RuntimeAutoSegmentLearner
    {
        private const double DefaultWindowSeconds = 1.0;
        private const double FloatTolerance = 0.01;

        private readonly double _windowSeconds;
        private readonly Dictionary<string, MainState> _states = new(StringComparer.Ordinal);

        public RuntimeAutoSegmentLearner(double windowSeconds = DefaultWindowSeconds)
        {
            _windowSeconds = Math.Max(0.1, windowSeconds);
        }

        public void Clear()
        {
            _states.Clear();
        }

        public void TrackNewMain(string mainCounter, double now)
        {
            if (string.IsNullOrWhiteSpace(mainCounter)) return;

            _states[mainCounter] = new MainState(mainCounter)
            {
                LastSeenAt = now
            };
        }

        public RuntimeAutoSegmentMark MarkMainHitMin(string mainCounter, double now)
        {
            if (!_states.TryGetValue(mainCounter, out var state))
            {
                return new(false, mainCounter, 0);
            }

            state.AttemptId++;
            state.WindowEndsAt = now + _windowSeconds;
            state.Candidates.Clear();
            return new(true, mainCounter, state.AttemptId);
        }

        public RuntimeAutoSegmentObservation ObserveCounter(
            string counterName,
            float currentValue,
            float? previousValue,
            float? maxValue,
            double now,
            Func<string, string, bool> namesMatch)
        {
            if (string.IsNullOrWhiteSpace(counterName)) return new(false);
            if (IsDangerousCandidateName(counterName)) return new(false);

            var mode = TryResolveMode(currentValue, previousValue, maxValue);
            if (mode is null) return new(false);

            var suppress = false;
            foreach (var state in _states.Values)
            {
                if (state.AttemptId <= 0) continue;
                if (now > state.WindowEndsAt) continue;
                if (namesMatch(counterName, state.MainCounter)) continue;

                state.Candidates[(counterName, mode.Value)] = new SegmentCandidate(counterName, mode.Value);
                suppress = true;
            }

            return new(suppress);
        }

        public RuntimeAutoSegmentDecision Finalize(string mainCounter, int attemptId, double now)
        {
            if (!_states.TryGetValue(mainCounter, out var state) || state.AttemptId != attemptId)
            {
                return new(RuntimeAutoSegmentDecisionStatus.None, mainCounter, null, 0, "not tracked");
            }

            var candidates = state.Candidates.Values
                .DistinctBy(candidate => (candidate.SegmentCounter, candidate.Mode))
                .ToList();

            var finalizedAfterWindow = now > state.WindowEndsAt;
            state.Candidates.Clear();
            state.WindowEndsAt = 0;

            if (candidates.Count == 0)
            {
                return new(RuntimeAutoSegmentDecisionStatus.None, mainCounter, null, 0, "no segment candidates");
            }

            if (candidates.Count > 1)
            {
                return new(RuntimeAutoSegmentDecisionStatus.Ambiguous, mainCounter, null, 0, "multiple segment candidates");
            }

            var learned = candidates[0];
            _states.Remove(mainCounter);
            return new(
                RuntimeAutoSegmentDecisionStatus.Learned,
                mainCounter,
                learned.SegmentCounter,
                learned.Mode,
                finalizedAfterWindow ? "learned after window" : "learned");
        }

        private static int? TryResolveMode(float currentValue, float? previousValue, float? maxValue)
        {
            if (currentValue < 0) return null;

            if (previousValue is { } previous)
            {
                var delta = currentValue - previous;
                if (Math.Abs(delta + 1) <= FloatTolerance) return 1;
                if (Math.Abs(delta - 1) <= FloatTolerance) return 2;
                return null;
            }

            if (maxValue is not { } max || max <= 0 || max > 64) return null;

            var looksLikeSubtract = Math.Abs(max - currentValue - 1) <= FloatTolerance;
            var looksLikeAdd = Math.Abs(currentValue - 1) <= FloatTolerance;
            if (looksLikeSubtract == looksLikeAdd) return null;
            return looksLikeSubtract ? 1 : 2;
        }

        private static bool IsDangerousCandidateName(string counterName)
        {
            var name = counterName.ToLowerInvariant();
            return name.Contains("attack", StringComparison.Ordinal)
                || name.Contains("crystal", StringComparison.Ordinal)
                || name.Contains("item", StringComparison.Ordinal)
                || name.Contains("laser", StringComparison.Ordinal)
                || name.Contains("overlay", StringComparison.Ordinal)
                || name.Contains("skill", StringComparison.Ordinal)
                || name.Contains("summon", StringComparison.Ordinal)
                || name.Contains("text", StringComparison.Ordinal)
                || name.Contains("timer", StringComparison.Ordinal)
                || name.Contains("track", StringComparison.Ordinal)
                || name.Contains("turn", StringComparison.Ordinal);
        }

        private sealed class MainState(string mainCounter)
        {
            public string MainCounter { get; } = mainCounter;
            public double LastSeenAt { get; init; }
            public int AttemptId { get; set; }
            public double WindowEndsAt { get; set; }
            public Dictionary<(string SegmentCounter, int Mode), SegmentCandidate> Candidates { get; } = new();
        }

        private sealed record SegmentCandidate(string SegmentCounter, int Mode);
    }
}
