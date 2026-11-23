using UnityEngine;

namespace K13A.TSMP.Udon
{
    public enum GpuOutputInput
    {
        NetworkFrame,
        TransSyncField
    }

    [AddComponentMenu("TSMP/GPU Output")]
    public sealed class TSMPGpuOutput : TSMPBehaviour
    {
        public GpuOutputInput input = GpuOutputInput.TransSyncField;
        [Range(0, 65535)] public int networkId;
        [Tooltip("Unsigned wire hash shown in the TransSync binding table.")]
        public uint variableHash;
        [Tooltip("Pass 0 receives a validated TSMP byte view through _MainTex.")]
        public Material material;
        public RenderTexture destination;

        public void RenderView(RenderTexture bytes, Vector3Int view, uint streamId, uint frameIndex)
        {
            if (!enabled || !gameObject.activeInHierarchy || material == null || destination == null ||
                bytes == null || bytes == destination)
                return;

            material.SetVector("_TSMPByteView", new Vector4(view.x, view.y, bytes.width, view.z));
            material.SetVector("_TSMPFrameInfo", new Vector4(streamId & 65535u, streamId >> 16,
                frameIndex & 65535u, frameIndex >> 16));
            material.SetVector("_TSMPOutputSize", new Vector4(destination.width, destination.height, 0f, 0f));
            GraphicsBridge.Blit(bytes, destination, material, 0);
        }
    }
}
