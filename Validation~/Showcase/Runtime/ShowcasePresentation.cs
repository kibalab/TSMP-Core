#if !UDONSHARP && !COMPILER_UDONSHARP
using UnityEngine;
using UnityEngine.UI;

// Three live views with a plain label row. Preserve each view's aspect ratio.
public sealed class ShowcasePresentation : MonoBehaviour
{
    public Texture sourceView, streamView, receiverView;
    const int Width = 1440, Height = 528, UiLayer = 10;
    Camera uiCamera;
    Canvas canvas;
    RenderTexture recordingTarget;
    RawImage sourceImage, streamImage, receiverImage;
    Font font;

    public RenderTexture RecordingTarget { get { Prepare(); return recordingTarget; } }

    public void RenderFrame()
    {
        Prepare();
        sourceImage.texture = sourceView;
        streamImage.texture = streamView;
        receiverImage.texture = receiverView;
        sourceImage.enabled = sourceView != null;
        streamImage.enabled = streamView != null;
        receiverImage.enabled = receiverView != null;
        Canvas.ForceUpdateCanvases();
        uiCamera.Render();
    }

    void Prepare()
    {
        if (recordingTarget != null) return;
        recordingTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
        {
            name = "TSMP three views", antiAliasing = 1, filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false
        };
        if (!recordingTarget.Create()) throw new System.InvalidOperationException("Showcase recording target could not be created");
        var cameraObject = new GameObject("Recording camera", typeof(Camera));
        cameraObject.layer = UiLayer; cameraObject.transform.SetParent(transform, false);
        cameraObject.transform.localPosition = new Vector3(0, 0, -10);
        uiCamera = cameraObject.GetComponent<Camera>();
        uiCamera.enabled = false; uiCamera.orthographic = true; uiCamera.orthographicSize = Height * .5f;
        uiCamera.aspect = Width / (float)Height; uiCamera.nearClipPlane = .01f; uiCamera.farClipPlane = 100;
        uiCamera.clearFlags = CameraClearFlags.SolidColor; uiCamera.backgroundColor = Color.black;
        uiCamera.cullingMask = 1 << UiLayer; uiCamera.allowHDR = false; uiCamera.allowMSAA = false;
        uiCamera.targetTexture = recordingTarget;

        var canvasObject = new GameObject("Three views", typeof(RectTransform), typeof(Canvas));
        canvasObject.layer = UiLayer; canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera; canvas.planeDistance = 1; canvas.sortingOrder = 100;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Arial" }, 24);
        Label("Sender", new Rect(0, 0, 480, 48));
        Label("Frame stream", new Rect(480, 0, 480, 48));
        Label("Receiver", new Rect(960, 0, 480, 48));
        Label("-->", new Rect(452, 0, 56, 48));
        Label("-->", new Rect(932, 0, 56, 48));
        sourceImage = View("Sender", new Rect(0, 48, 480, 480));
        streamImage = View("Encoded frame", new Rect(480, 153, 480, 270));
        receiverImage = View("Receiver", new Rect(960, 48, 480, 480));
    }

    RawImage View(string name, Rect rectangle)
    {
        var item = new GameObject(name, typeof(RectTransform), typeof(RawImage)); item.layer = UiLayer;
        var rt = item.GetComponent<RectTransform>(); rt.SetParent(canvas.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(rectangle.x, -rectangle.y); rt.sizeDelta = rectangle.size;
        var image = item.GetComponent<RawImage>(); image.raycastTarget = false; return image;
    }

    void Label(string value, Rect rectangle)
    {
        var item = new GameObject(value, typeof(RectTransform), typeof(Text)); item.layer = UiLayer;
        var rt = item.GetComponent<RectTransform>(); rt.SetParent(canvas.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(rectangle.x, -rectangle.y); rt.sizeDelta = rectangle.size;
        var text = item.GetComponent<Text>(); text.font = font; text.fontSize = 24;
        text.text = value; text.color = new Color(.85f, .85f, .85f);
        text.alignment = TextAnchor.MiddleCenter; text.supportRichText = false; text.raycastTarget = false;
    }

    void OnDestroy()
    {
        if (uiCamera != null) { uiCamera.targetTexture = null; Destroy(uiCamera.gameObject); }
        if (canvas != null) Destroy(canvas.gameObject);
        if (recordingTarget != null) { recordingTarget.Release(); Destroy(recordingTarget); }
        if (font != null) Destroy(font);
    }
}
#endif
