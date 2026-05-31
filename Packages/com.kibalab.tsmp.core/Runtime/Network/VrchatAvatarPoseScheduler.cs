using UnityEngine;

namespace K13A.TSMP.Udon
{
    public static class VrchatAvatarPoseScheduler
    {
        private const int TrackingPointsMode = 1;
        private const int HumanoidBonesMode = 2;

        public static int GetPoseRecordCount(int mode, bool includeFingerBones, int humanoidBoneCount, int leftFingerBoneCount, int rightFingerBoneCount, int trackingPointCount)
        {
            if (mode == HumanoidBonesMode)
                return humanoidBoneCount + (includeFingerBones ? leftFingerBoneCount + rightFingerBoneCount : 0);

            if (mode == TrackingPointsMode)
                return trackingPointCount;

            return 0;
        }

        public static void AssignPoseRecordCounts(
            int[] playerRecordCounts,
            int[] validPlayerIndices,
            bool[] playerRootPoseIncluded,
            int mode,
            int totalRecordCount,
            int playerCount,
            int sequence,
            int humanoidRecordBudgetPerFrame,
            int humanoidRootPoseRecordCount)
        {
            if (playerRecordCounts == null || validPlayerIndices == null)
                return;

            ResetPoseRecordCounts(playerRecordCounts, validPlayerIndices, playerCount);

            if (playerCount <= 0 || totalRecordCount <= 0)
                return;

            if (mode != HumanoidBonesMode)
            {
                AssignAllPoseRecords(playerRecordCounts, validPlayerIndices, totalRecordCount, playerCount);
                return;
            }

            AssignHumanoidPoseRecordCounts(playerRecordCounts, validPlayerIndices, playerRootPoseIncluded, totalRecordCount, playerCount, sequence, humanoidRecordBudgetPerFrame, humanoidRootPoseRecordCount);
        }

        public static int SuppressRootOnlyHumanoidEntries(
            int[] validPlayerIndices,
            bool[] playerRootPoseIncluded,
            int[] playerRecordCounts,
            int mode,
            int playerCount,
            int totalRecordCount,
            int humanoidRootPoseRecordCount)
        {
            if (mode != HumanoidBonesMode)
                return 0;

            if (validPlayerIndices == null || playerRootPoseIncluded == null || playerRecordCounts == null)
                return 0;

            int suppressed = 0;
            int rootBundleCount = GetHumanoidRootPoseRecordCount(totalRecordCount, humanoidRootPoseRecordCount);
            for (int i = 0; i < playerCount && i < validPlayerIndices.Length; i++)
            {
                int playerIndex = validPlayerIndices[i];
                if (!ShouldSuppressRootOnlyHumanoidEntry(playerRootPoseIncluded, playerRecordCounts, playerIndex, rootBundleCount))
                    continue;

                playerRootPoseIncluded[playerIndex] = false;
                suppressed++;
            }

            return suppressed;
        }

        public static int GetHumanoidRecordStart(int playerId, int sequence, int totalRecordCount, int recordCount)
        {
            if (totalRecordCount <= 0 || recordCount <= 0 || recordCount >= totalRecordCount)
                return 0;

            int sequenceStart = sequence % totalRecordCount;
            int playerStart = GetNonNegativeModulo(playerId, totalRecordCount);
            int start = sequenceStart + playerStart;
            if (start >= totalRecordCount)
                start -= totalRecordCount;
            return start;
        }

        public static int GetHumanoidRecordBoneId(int recordIndex, int[] humanoidBoneIds, bool includeFingerBones, int[] leftFingerBoneIds, int[] rightFingerBoneIds, int fallbackBoneId)
        {
            int index = recordIndex;
            if (humanoidBoneIds != null && index >= 0 && index < humanoidBoneIds.Length)
                return humanoidBoneIds[index];

            int humanoidCount = humanoidBoneIds == null ? 0 : humanoidBoneIds.Length;
            index -= humanoidCount;
            if (includeFingerBones)
            {
                if (leftFingerBoneIds != null && index >= 0 && index < leftFingerBoneIds.Length)
                    return leftFingerBoneIds[index];

                int leftCount = leftFingerBoneIds == null ? 0 : leftFingerBoneIds.Length;
                index -= leftCount;
                if (rightFingerBoneIds != null && index >= 0 && index < rightFingerBoneIds.Length)
                    return rightFingerBoneIds[index];
            }

            return fallbackBoneId;
        }

        public static bool ShouldSendKeyframe(int cachedPlayerId, int lastSequence, int playerId, int sequence, int interval)
        {
            if (cachedPlayerId != playerId)
                return true;

            if (lastSequence < 0)
                return true;

            if (interval < 1)
                interval = 1;

            return GetSequenceElapsed(sequence, lastSequence) >= interval;
        }

        public static bool ShouldWriteRootPose(
            int cachedPlayerId,
            int lastSequence,
            int playerId,
            int sequence,
            int interval,
            Vector3 lastPosition,
            Quaternion lastRotation,
            Vector3 position,
            Quaternion rotation,
            float positionThreshold,
            float rotationThresholdDegrees)
        {
            if (ShouldSendKeyframe(cachedPlayerId, lastSequence, playerId, sequence, interval))
                return true;

            if (positionThreshold <= 0f)
                return true;

            Vector3 positionDelta = position - lastPosition;
            if (positionDelta.sqrMagnitude > positionThreshold * positionThreshold)
                return true;

            if (rotationThresholdDegrees <= 0f)
                return true;

            float dot = rotation.x * lastRotation.x + rotation.y * lastRotation.y + rotation.z * lastRotation.z + rotation.w * lastRotation.w;
            if (dot < 0f)
                dot = -dot;

            float radians = rotationThresholdDegrees * 0.0174532924f;
            float dotThreshold = Mathf.Cos(radians * 0.5f);
            return dot < dotThreshold;
        }

        private static void ResetPoseRecordCounts(int[] playerRecordCounts, int[] validPlayerIndices, int playerCount)
        {
            for (int i = 0; i < playerCount && i < validPlayerIndices.Length; i++)
            {
                int playerIndex = validPlayerIndices[i];
                if (playerIndex >= 0 && playerIndex < playerRecordCounts.Length)
                    playerRecordCounts[playerIndex] = 0;
            }
        }

        private static void AssignAllPoseRecords(int[] playerRecordCounts, int[] validPlayerIndices, int totalRecordCount, int playerCount)
        {
            for (int i = 0; i < playerCount && i < validPlayerIndices.Length; i++)
            {
                int playerIndex = validPlayerIndices[i];
                if (playerIndex >= 0 && playerIndex < playerRecordCounts.Length)
                    playerRecordCounts[playerIndex] = totalRecordCount;
            }
        }

        private static void AssignHumanoidPoseRecordCounts(
            int[] playerRecordCounts,
            int[] validPlayerIndices,
            bool[] playerRootPoseIncluded,
            int totalRecordCount,
            int playerCount,
            int sequence,
            int humanoidRecordBudgetPerFrame,
            int humanoidRootPoseRecordCount)
        {
            int maxRecords = totalRecordCount * playerCount;
            int budget = GetHumanoidRecordBudget(humanoidRecordBudgetPerFrame, maxRecords);
            if (budget <= 0)
                return;

            if (budget >= maxRecords)
            {
                AssignAllPoseRecords(playerRecordCounts, validPlayerIndices, totalRecordCount, playerCount);
                return;
            }

            int start = sequence % playerCount;
            int rootBundleCount = GetHumanoidRootPoseRecordCount(totalRecordCount, humanoidRootPoseRecordCount);
            int remaining = AssignHumanoidRootPoseBundles(playerRecordCounts, validPlayerIndices, playerRootPoseIncluded, totalRecordCount, playerCount, start, rootBundleCount, budget);
            DistributeHumanoidPoseRecords(playerRecordCounts, validPlayerIndices, totalRecordCount, playerCount, start, remaining);
        }

        private static int GetHumanoidRecordBudget(int humanoidRecordBudgetPerFrame, int maxRecords)
        {
            int budget = humanoidRecordBudgetPerFrame;
            if (budget <= 0)
                budget = maxRecords;
            if (budget > maxRecords)
                budget = maxRecords;

            return budget;
        }

        private static int AssignHumanoidRootPoseBundles(
            int[] playerRecordCounts,
            int[] validPlayerIndices,
            bool[] playerRootPoseIncluded,
            int totalRecordCount,
            int playerCount,
            int start,
            int rootBundleCount,
            int budget)
        {
            int remaining = budget;
            for (int i = 0; i < playerCount; i++)
            {
                if (remaining < rootBundleCount)
                    break;

                int order = WrapPlayerOrder(start + i, playerCount);
                if (order < 0 || order >= validPlayerIndices.Length)
                    continue;

                int playerIndex = validPlayerIndices[order];
                if (!CanAssignRootPoseBundle(playerRootPoseIncluded, playerIndex))
                    continue;

                int assign = rootBundleCount;
                if (assign > totalRecordCount)
                    assign = totalRecordCount;
                if (playerIndex < 0 || playerIndex >= playerRecordCounts.Length)
                    continue;

                playerRecordCounts[playerIndex] = assign;
                remaining -= assign;
            }

            return remaining;
        }

        private static void DistributeHumanoidPoseRecords(int[] playerRecordCounts, int[] validPlayerIndices, int totalRecordCount, int playerCount, int start, int remaining)
        {
            int cursor = start;
            int guard = 0;
            int maxIterations = playerCount * totalRecordCount;
            while (remaining > 0 && guard < maxIterations)
            {
                if (cursor < validPlayerIndices.Length)
                {
                    int playerIndex = validPlayerIndices[cursor];
                    if (playerIndex >= 0 && playerIndex < playerRecordCounts.Length && playerRecordCounts[playerIndex] < totalRecordCount)
                    {
                        playerRecordCounts[playerIndex]++;
                        remaining--;
                    }
                }

                cursor++;
                if (cursor >= playerCount)
                    cursor = 0;
                guard++;
            }
        }

        private static bool CanAssignRootPoseBundle(bool[] playerRootPoseIncluded, int playerIndex)
        {
            if (playerRootPoseIncluded == null)
                return false;

            if (playerIndex < 0 || playerIndex >= playerRootPoseIncluded.Length)
                return false;

            return playerRootPoseIncluded[playerIndex];
        }

        private static int GetHumanoidRootPoseRecordCount(int totalRecordCount, int humanoidRootPoseRecordCount)
        {
            if (totalRecordCount <= 0)
                return 0;

            int count = humanoidRootPoseRecordCount;
            if (count < 1)
                count = 1;
            if (count > totalRecordCount)
                count = totalRecordCount;

            return count;
        }

        private static bool ShouldSuppressRootOnlyHumanoidEntry(bool[] playerRootPoseIncluded, int[] playerRecordCounts, int playerIndex, int rootBundleCount)
        {
            if (playerIndex < 0)
                return false;

            if (playerIndex >= playerRootPoseIncluded.Length || playerIndex >= playerRecordCounts.Length)
                return false;

            if (!playerRootPoseIncluded[playerIndex])
                return false;

            return playerRecordCounts[playerIndex] < rootBundleCount;
        }

        private static int WrapPlayerOrder(int order, int playerCount)
        {
            if (playerCount <= 0)
                return 0;

            while (order >= playerCount)
                order -= playerCount;

            while (order < 0)
                order += playerCount;

            return order;
        }

        private static int GetNonNegativeModulo(int value, int modulo)
        {
            if (modulo <= 0)
                return 0;

            int result = value % modulo;
            if (result < 0)
                result += modulo;

            return result;
        }

        private static int GetSequenceElapsed(int sequence, int lastSequence)
        {
            int elapsed = sequence - lastSequence;
            if (elapsed < 0)
                elapsed += 65536;

            return elapsed;
        }
    }
}
