// 視野データの保存と読み込み。
// ファイルの中身は Web版の localStorage（キー hfa-sim-data-v2）と同じ形 {"od":[76個],"os":[76個]}。
using System;
using System.IO;
using UnityEngine;

[Serializable]
public class HfaData
{
    public float[] od;
    public float[] os;

    public static HfaData Normal()
    {
        var d = new HfaData { od = new float[HfaField.N], os = new float[HfaField.N] };
        for (int i = 0; i < HfaField.N; i++) { d.od[i] = 30; d.os[i] = 30; }
        return d;
    }
}

public static class HfaStore
{
    public const string FileName = "hfa-sim-data-v2.json";

    public static string FilePath { get { return Path.Combine(Application.persistentDataPath, FileName); } }

    public static HfaData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var d = JsonUtility.FromJson<HfaData>(File.ReadAllText(FilePath));
                if (d != null && d.od != null && d.os != null &&
                    d.od.Length == HfaField.N && d.os.Length == HfaField.N)
                {
                    for (int i = 0; i < HfaField.N; i++)
                    {
                        d.od[i] = HfaField.Clamp(d.od[i]);
                        d.os[i] = HfaField.Clamp(d.os[i]);
                    }
                    return d;
                }
                Debug.LogWarning("[HFA] 保存データの形式が違うため、正常視野で開始します: " + FilePath);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[HFA] 保存データを読めませんでした: " + e.Message);
        }
        return HfaData.Normal();
    }

    public static void Save(HfaData d)
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(d)); }
        catch (Exception e) { Debug.LogWarning("[HFA] 保存に失敗しました: " + e.Message); }
    }
}
