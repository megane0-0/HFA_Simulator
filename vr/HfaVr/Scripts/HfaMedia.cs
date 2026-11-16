// 360度の写真・動画（正距円筒図法、横:縦 = 2:1、モノラル）を読み込む。
// StreamingAssets/360/ に置いた jpg / png / mp4 などを名前順に切り替える。
// 何も置いていなければ、動作確認用の模様を自動生成する。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

public class HfaMedia : MonoBehaviour
{
    public Texture Current { get; private set; }
    public string CurrentName { get; private set; }

    readonly List<string> files = new List<string>();
    int index = -1;
    Texture2D photo;
    Texture2D pattern;
    RenderTexture videoRt;
    VideoPlayer player;

    static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png" };
    static readonly string[] VideoExt = { ".mp4", ".mov", ".webm", ".m4v" };

    public void Scan(string folder)
    {
        files.Clear();
        string dir = Path.Combine(Application.streamingAssetsPath, folder);
        try
        {
            if (Directory.Exists(dir))
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(ImageExt, e) >= 0 || Array.IndexOf(VideoExt, e) >= 0) files.Add(f);
                }
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) { Debug.LogWarning("[HFA] 映像フォルダを読めませんでした: " + ex.Message); }
        index = -1;
        Next();
    }

    public void Next()
    {
        StopVideo();
        if (files.Count == 0) { UsePattern(); return; }
        index = (index + 1) % files.Count;
        string f = files[index];
        CurrentName = Path.GetFileName(f);
        string e = Path.GetExtension(f).ToLowerInvariant();
        try
        {
            if (Array.IndexOf(ImageExt, e) >= 0) LoadPhoto(f); else PlayVideo(f);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[HFA] 映像を読めませんでした（" + CurrentName + "）: " + ex.Message);
            UsePattern();
        }
    }

    static void Setup(Texture t)
    {
        t.wrapModeU = TextureWrapMode.Repeat;   // 経度方向は一周つながる
        t.wrapModeV = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Trilinear;    // ぼかしはミップマップで作るので必須
        t.anisoLevel = 0;
    }

    void LoadPhoto(string path)
    {
        if (photo != null) Destroy(photo);
        photo = new Texture2D(2, 2, TextureFormat.RGBA32, true);   // ミップマップあり
        if (!photo.LoadImage(File.ReadAllBytes(path), true)) throw new Exception("画像として読めません");
        Setup(photo);
        Current = photo;
    }

    void PlayVideo(string path)
    {
        if (videoRt == null)
        {
            videoRt = new RenderTexture(4096, 2048, 0, RenderTextureFormat.ARGB32);
            videoRt.useMipMap = true;
            videoRt.autoGenerateMips = true;
            Setup(videoRt);
            videoRt.Create();
        }
        if (player == null)
        {
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.aspectRatio = VideoAspectRatio.Stretch;
        }
        player.targetTexture = videoRt;
        player.source = VideoSource.Url;
        player.url = path;
        player.Play();
        Current = videoRt;
    }

    void StopVideo()
    {
        if (player != null && player.isPlaying) player.Stop();
    }

    // 動作確認用の模様: 空と地面、10度ごとの格子、色の柱、細かい縞（ぼけ具合が分かるように）
    void UsePattern()
    {
        CurrentName = "確認用の模様（StreamingAssets/360 に映像なし）";
        if (pattern == null)
        {
            const int W = 2048, H = 1024;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float lat = (y + 0.5f) / H * 180f - 90f;
                for (int x = 0; x < W; x++)
                {
                    float lon = (x + 0.5f) / W * 360f - 180f;
                    Color c = lat > 0
                        ? Color.Lerp(new Color(0.75f, 0.85f, 0.95f), new Color(0.25f, 0.5f, 0.9f), lat / 90f)
                        : Color.Lerp(new Color(0.55f, 0.55f, 0.5f), new Color(0.3f, 0.35f, 0.3f), -lat / 90f);
                    float a = Mathf.Abs(Mathf.Repeat(lon + 5f, 10f) - 5f);
                    float b = Mathf.Abs(Mathf.Repeat(lat + 5f, 10f) - 5f);
                    if (a < 0.15f || b < 0.15f) c = Color.Lerp(c, Color.white, 0.8f);
                    if (lat > -20f && lat < 25f)
                    {
                        int k = Mathf.FloorToInt((lon + 180f) / 30f);
                        float inCol = Mathf.Repeat(lon + 180f, 30f);
                        if (inCol > 11f && inCol < 19f)
                        {
                            c = Color.HSVToRGB((k % 12) / 12f, 0.75f, 0.9f);
                            if (lat > 5f && lat < 15f && ((x / 4) % 2 == 0)) c = Color.black;        // 細かい縦縞
                            if (lat > -10f && lat < 0f && (((x / 16) + (y / 16)) % 2 == 0)) c = Color.white; // 市松
                        }
                    }
                    px[y * W + x] = c;
                }
            }
            pattern = new Texture2D(W, H, TextureFormat.RGBA32, true);
            pattern.SetPixels32(px);
            pattern.Apply(true);
            Setup(pattern);
        }
        Current = pattern;
    }
}
