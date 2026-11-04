#if !UDONSHARP && !COMPILER_UDONSHARP
using UnityEngine;
using UnityEngine.UI;

// Renders only supplied live demo textures and diagnostics into an independent target.
public sealed class ShowcasePresentation : MonoBehaviour
{
    public Texture sourceView, streamView, receiverView;
    public string senderDetails = "", streamDetails = "", receiverDetails = "";
    public string senderState = "", receiverState = "";
    public string headline = "State \u2192 pixels \u2192 state";
    public string status = "Connecting the sender and receiver";
    public bool holding, crcVerified;
    public int senderPid, receiverPid;

    const int Width = 1600, Height = 900, UiLayer = 10;
    static readonly Color Background = Hex(0x080f1d), Card = Hex(0x101b2b), Border = Hex(0x26364b);
    static readonly Color White = Hex(0xf4f7fc), Muted = Hex(0x93a5bc), Amber = Hex(0xffc16b);
    static readonly Color Purple = Hex(0xa79bff), Cyan = Hex(0x6ee4ef);
    Camera uiCamera;
    Canvas canvas;
    RectTransform canvasRoot;
    RenderTexture recordingTarget;
    Font font;
    Text headlineLabel, statusLabel, sourcePidLabel, receiverPidLabel;
    Text sourceStateLabel, receiverStateLabel, sourceDetailsLabel, streamDetailsLabel, receiverDetailsLabel, crcLabel;
    Image statusDot, crcBackground;
    ViewElements sourceElements, streamElements, receiverElements;

    sealed class ViewElements
    {
        public RawImage image;
        public Text waiting;
        public AspectRatioFitter aspect;
    }

    public RenderTexture RecordingTarget { get { Prepare(); return recordingTarget; } }
    static Color Hex(uint v) => new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1);
    static Color Dim(Color c) => new Color(c.r * .17f, c.g * .17f, c.b * .17f, 1);

    public void RenderFrame()
    {
        Prepare();
        headlineLabel.text = headline ?? "";
        statusLabel.text = status ?? "";
        Color stateColor = holding ? Amber : Cyan;
        statusLabel.color = stateColor; statusDot.color = stateColor;
        sourcePidLabel.text = senderPid > 0 ? "NATIVE UNITY  /  PID " + senderPid : "NATIVE UNITY  /  CONNECTING";
        receiverPidLabel.text = receiverPid > 0 ? "NATIVE UNITY  /  PID " + receiverPid : "NATIVE UNITY";
        sourceStateLabel.text = senderState ?? ""; receiverStateLabel.text = receiverState ?? "";
        sourceDetailsLabel.text = senderDetails ?? ""; streamDetailsLabel.text = streamDetails ?? "";
        receiverDetailsLabel.text = receiverDetails ?? "";
        crcLabel.text = crcVerified ? (holding ? "LAST VALID FRAME  /  CRC32 VERIFIED" : "CRC32 VERIFIED") : "WAITING FOR A VALID FRAME";
        Color crcColor = crcVerified ? Cyan : Amber;
        crcLabel.color = crcColor; crcBackground.color = Dim(crcColor);
        SetView(sourceElements, sourceView); SetView(streamElements, streamView); SetView(receiverElements, receiverView);
        Canvas.ForceUpdateCanvases();
        uiCamera.Render();
    }

    void Prepare()
    {
        if (recordingTarget != null) return;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Liberation Sans" }, 48);
        recordingTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
        {
            name = "TSMP Showcase Recording 1600x900", antiAliasing = 1, filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false
        };
        if (!recordingTarget.Create()) throw new System.InvalidOperationException("Showcase recording target could not be created");
        var cameraObject = new GameObject("TSMP Showcase UI Camera", typeof(Camera));
        cameraObject.layer = UiLayer; cameraObject.transform.SetParent(transform, false);
        cameraObject.transform.localPosition = new Vector3(0, 0, -10);
        uiCamera = cameraObject.GetComponent<Camera>();
        uiCamera.enabled = false; uiCamera.orthographic = true; uiCamera.orthographicSize = Height * .5f;
        uiCamera.aspect = Width / (float)Height; uiCamera.nearClipPlane = .01f; uiCamera.farClipPlane = 100;
        uiCamera.clearFlags = CameraClearFlags.SolidColor; uiCamera.backgroundColor = Background;
        uiCamera.cullingMask = 1 << UiLayer; uiCamera.allowHDR = false; uiCamera.allowMSAA = false;
        uiCamera.targetTexture = recordingTarget; uiCamera.rect = new Rect(0, 0, 1, 1);

        var canvasObject = new GameObject("TSMP Showcase Recording Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.layer = UiLayer; canvasObject.transform.SetParent(transform, false);
        canvasRoot = canvasObject.GetComponent<RectTransform>();
        canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera; canvas.planeDistance = 1; canvas.sortingOrder = 100; canvas.pixelPerfect = false;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1; scaler.referencePixelsPerUnit = 100;

        Fill("Background", new Rect(0, 0, Width, Height), Background);
        Label("TSMP", new Rect(48, 27, 182, 70), "TSMP", 48, White, true);
        Label("Protocol name", new Rect(234, 54, 650, 24), "TRANS SYNC MEDIA PROTOCOL", 14, Muted, true);
        Badge("Two processes", new Rect(1281, 43, 271, 34), "TWO INDEPENDENT PROCESSES", Purple);
        headlineLabel = Label("Headline", new Rect(48, 112, 1504, 48), headline, 34, White);
        Fill("Status divider", new Rect(48, 170, 1504, 1), Border);
        statusDot = Fill("Status dot", new Rect(49, 184, 7, 7), Cyan);
        statusLabel = Label("Status", new Rect(69, 177, 1482, 28), status, 18, Cyan);
        sourcePidLabel = DrawCard(48, "SENDER", "NATIVE UNITY  /  CONNECTING", Amber);
        DrawCard(556, "FRAME STREAM", "THE IMAGE CARRIES THE STATE", Purple);
        receiverPidLabel = DrawCard(1064, "RECEIVER", "NATIVE UNITY", Cyan);
        sourceElements = View("Source view", new Rect(64, 313, 456, 376));
        receiverElements = View("Receiver view", new Rect(1080, 313, 456, 376));
        Fill("Stream border", new Rect(571, 350, 458, 259), Border);
        streamElements = View("Stream view", new Rect(572, 351, 456, 257));
        Label("Stream explanation", new Rect(572, 626, 456, 35), "Actual encoded video frame", 18, Muted, false, TextAnchor.MiddleCenter);
        crcBackground = Fill("CRC background", new Rect(572, 675, 456, 33), Dim(Amber));
        crcLabel = Label("CRC state", new Rect(572, 675, 456, 33), "WAITING FOR A VALID FRAME", 15, Amber, false, TextAnchor.MiddleCenter);
        sourceStateLabel = Label("Source state", new Rect(72, 705, 440, 34), "", 21, White, true);
        receiverStateLabel = Label("Receiver state", new Rect(1088, 705, 440, 34), "", 21, White, true);
        sourceDetailsLabel = Label("Source details", new Rect(72, 748, 440, 46), "", 18, Muted);
        streamDetailsLabel = Label("Stream details", new Rect(580, 732, 440, 62), "", 18, Muted);
        receiverDetailsLabel = Label("Receiver details", new Rect(1088, 748, 440, 46), "", 18, Muted);
        Label("First flow arrow", new Rect(535, 460, 25, 46), "\u2192", 24, White, true);
        Label("Second flow arrow", new Rect(1043, 460, 25, 46), "\u2192", 24, White, true);
        Fill("Footer divider", new Rect(48, 833, 1504, 1), Border);
        Label("Scope", new Rect(48, 851, 1504, 30), "Native Unity \u00b7 two local processes \u00b7 raw-frame transport", 18, Muted, false, TextAnchor.MiddleCenter);
        Canvas.ForceUpdateCanvases();
    }

    RectTransform Rectangle(string name, Rect rectangle, Transform parent)
    {
        var item = new GameObject(name, typeof(RectTransform)); item.layer = UiLayer;
        var rt = item.GetComponent<RectTransform>(); rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(rectangle.x, -rectangle.y); rt.sizeDelta = rectangle.size;
        return rt;
    }

    Image Fill(string name, Rect rectangle, Color color)
    {
        var image = Rectangle(name, rectangle, canvasRoot).gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false; return image;
    }

    Text Label(string name, Rect rectangle, string value, int size, Color color, bool bold = false, TextAnchor alignment = TextAnchor.UpperLeft)
    {
        var text = Rectangle(name, rectangle, canvasRoot).gameObject.AddComponent<Text>();
        text.font = font; text.fontSize = size; text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.color = color; text.alignment = alignment; text.text = value ?? ""; text.supportRichText = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false;
        return text;
    }

    void Badge(string name, Rect rectangle, string value, Color accent)
    {
        Fill(name + " background", rectangle, Dim(accent));
        Label(name, rectangle, value, 15, accent, false, TextAnchor.MiddleCenter);
    }

    Text DrawCard(float x, string role, string subheading, Color accent)
    {
        Fill(role + " border", new Rect(x, 215, 488, 590), Border);
        Fill(role + " background", new Rect(x + 1, 216, 486, 588), Card);
        Fill(role + " accent", new Rect(x + 1, 216, 486, 3), accent);
        Label(role + " title", new Rect(x + 24, 239, 440, 34), role, 24, White, true);
        return Label(role + " subtitle", new Rect(x + 24, 278, 440, 25), subheading, 15, Muted);
    }

    ViewElements View(string name, Rect rectangle)
    {
        var container = Fill(name + " background", rectangle, Hex(0x0b1523)).rectTransform;
        var viewObject = new GameObject(name, typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter)); viewObject.layer = UiLayer;
        var rt = viewObject.GetComponent<RectTransform>(); rt.SetParent(container, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var image = viewObject.GetComponent<RawImage>(); image.raycastTarget = false;
        var aspect = viewObject.GetComponent<AspectRatioFitter>(); aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        var waiting = Label(name + " waiting", rectangle, "Waiting for the first frame", 18, Muted, false, TextAnchor.MiddleCenter);
        return new ViewElements { image = image, aspect = aspect, waiting = waiting };
    }

    static void SetView(ViewElements elements, Texture value)
    {
        elements.image.texture = value; elements.image.enabled = value != null; elements.waiting.enabled = value == null;
        if (value != null) elements.aspect.aspectRatio = value.width / (float)value.height;
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
