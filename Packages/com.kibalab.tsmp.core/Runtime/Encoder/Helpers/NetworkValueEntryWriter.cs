namespace K13A.TSMP
{
    public static class NetworkValueEntryWriter
    {
        public static int WriteVariableValue(byte[] buffer, int offset, uint variableHash, int valueType, object value)
        {
            int valueStartOffset = offset;
            int nextOffset = NetworkFrameWriter.BeginVariableValue(buffer, offset, variableHash, valueType);
            if (nextOffset < 0)
                return -1;

            nextOffset = NetworkValueWriter.WriteObject(buffer, nextOffset, valueType, value);
            return CompleteVariableValue(buffer, valueStartOffset, nextOffset);
        }

        public static int WriteRpcArgument(byte[] buffer, int offset, int valueType, object value)
        {
            int valueStartOffset = offset;
            int nextOffset = NetworkFrameWriter.BeginRpcArgument(buffer, offset, valueType);
            if (nextOffset < 0)
                return -1;

            nextOffset = NetworkValueWriter.WriteObject(buffer, nextOffset, valueType, value);
            return CompleteRpcArgument(buffer, valueStartOffset, nextOffset);
        }

        private static int CompleteVariableValue(byte[] buffer, int valueStartOffset, int nextOffset)
        {
            if (nextOffset < 0)
                return -1;
            if (!NetworkFrameWriter.EndVariableValue(buffer, valueStartOffset, nextOffset))
                return -1;

            return nextOffset;
        }

        private static int CompleteRpcArgument(byte[] buffer, int valueStartOffset, int nextOffset)
        {
            if (nextOffset < 0)
                return -1;
            if (!NetworkFrameWriter.EndRpcArgument(buffer, valueStartOffset, nextOffset))
                return -1;

            return nextOffset;
        }
    }

}
