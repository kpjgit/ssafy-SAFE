using System.Collections;
using UnityEngine;
using UnityEngine.U2D; // SpriteShapeController 필요시

[RequireComponent(typeof(SpriteMask))]
public class SpriteShapeToMask : MonoBehaviour
{
    [Header("Source (SpriteShape GameObject)")]
    public GameObject sourceObject; // Sprite Shape이 붙어있는 GameObject

    [Header("Render settings")]
    public int textureSize = 1024;           // 마스크 텍스처 해상도
    public LayerMask renderLayer;           // sourceObject를 올려둔 레이어 (카메라가 이 레이어만 렌더)
    public float padding = 0.1f;            // 바운딩 여유 (비율)

    [Header("When to update")]
    public bool generateOnStart = true;

    SpriteMask spriteMask;

    void Awake()
    {
        spriteMask = GetComponent<SpriteMask>();
    }

    void Start()
    {
        if (generateOnStart) StartCoroutine(GenerateMaskCoroutine());
    }

    // 호출용 공개 메서드
    public void GenerateMask()
    {
        StartCoroutine(GenerateMaskCoroutine());
    }

    IEnumerator GenerateMaskCoroutine()
    {
        if (sourceObject == null)
        {
            Debug.LogError("Source Object is null.");
            yield break;
        }

        // 1) 계산: sourceObject의 전체 bounds
        var renderers = sourceObject.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogError("No renderer found on sourceObject.");
            yield break;
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        // 패딩 적용
        b.Expand(new Vector3(b.size.x * padding, b.size.y * padding, 0));

        // 2) 임시 카메라와 RenderTexture 생성
        var rt = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 1;
        rt.Create();

        var camGO = new GameObject("TempMaskCam");
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.cullingMask = renderLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0); // 투명
        cam.targetTexture = rt;
        cam.allowHDR = false;
        cam.allowMSAA = false;

        // 카메라 위치/사이즈 설정
        float worldWidth = b.size.x;
        float worldHeight = b.size.y;
        float orthoSize = Mathf.Max(worldHeight / 2f, worldWidth / 2f / ((float)textureSize / textureSize));
        cam.orthographicSize = orthoSize;
        cam.transform.position = new Vector3(b.center.x, b.center.y, -10f); // Z는 카메라가 오브젝트를 바라보게
        cam.transform.rotation = Quaternion.identity;

        // Wait one frame (렌더링 안정 위해)
        yield return new WaitForEndOfFrame();

        // 3) 강제 렌더링
        cam.Render();

        // 4) RenderTexture -> Texture2D
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();

        RenderTexture.active = prev;

        // 5) 필요하면 세로 뒤집기 (ReadPixels로 인해 뒤집힐 수 있으므로 체크)
        // 만약 마스크가 뒤집혀 나온다면 아래 주석 해제
        // tex = FlipTextureVertical(tex);

        // 6) Sprite 생성 (pixelsPerUnit는 씬 스케일에 맞게 조절)
        float pixelsPerUnit = Mathf.Max(rt.width / worldWidth, 100f); // 간단 계산, 필요하면 조절
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                      new Vector2(0.5f, 0.5f), pixelsPerUnit);

        // 7) SpriteMask에 적용
        spriteMask.sprite = sprite;

        // 정리
        cam.targetTexture = null;
        rt.Release();
        Destroy(rt);
        Destroy(camGO);
    }

    // 유틸: 텍스처 수직 뒤집기 (필요하면 사용)
    Texture2D FlipTextureVertical(Texture2D original)
    {
        Texture2D flipped = new Texture2D(original.width, original.height);
        int w = original.width;
        int h = original.height;
        for (int y = 0; y < h; y++)
        {
            flipped.SetPixels(0, y, w, 1, original.GetPixels(0, h - y - 1, w, 1));
        }
        flipped.Apply();
        return flipped;
    }
}
