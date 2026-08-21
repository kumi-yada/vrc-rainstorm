using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDK3.Image;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// VRCUrlInputField で入力した画像URLを同期し、
/// 各クライアントが同じ画像をダウンロードして Material に適用するサンプルです。
/// さらに、極端に横長・縦長の画像でも、Xスケールが 0.5376 以下、Yスケールが 0.3024 以下になるようにサイズを調整します。
/// ClearImage() を呼ぶと全プレイヤーで画像がクリアされます。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ImageLoader : UdonSharpBehaviour
{
    [Header("UI 参照")]
    [Tooltip("URL 入力用の VRCUrlInputField (ローカルで入力される)")]
    public VRCUrlInputField urlInputField;

    [Tooltip("ダウンロードした画像を反映する Material")]
    public Material imageDisplay;

    [Tooltip("画面に表示するお知らせテキスト (例: 「読み込み中」等)")]
    public TMP_Text debugText;

    [Header("スケール調整")]
    [Tooltip("画像表示に用いるオブジェクトの Transform (Quad 等)")]
    public Transform imageTransform;

    [Tooltip(
        "目標とする Y 軸のサイズ (実際は 0.3024 以下に抑え、X 軸は 0.5376 以下になるように調整)"
    )]
    public float transformY = 0.2f; // 例: 0.2

    /// <summary>
    /// 同期対象となる画像URL。
    /// </summary>
    [UdonSynced]
    private VRCUrl syncedUrl;

    private VRCImageDownloader _imageDownloader;
    private IUdonEventReceiver _udonEventReceiver;
    private TextureInfo _rgbInfo;

    /// <summary>
    /// Y軸の上限 (固定値 0.3024)
    /// </summary>
    private const float MAX_TRANSFORM_Y = 0.3024f;

    /// <summary>
    /// X軸の上限 (固定値 0.5376)
    /// </summary>
    private const float MAX_TRANSFORM_X = 0.5376f;

    void Start()
    {
        _imageDownloader = new VRCImageDownloader();
        _udonEventReceiver = (IUdonEventReceiver)this;

        _rgbInfo = new TextureInfo();
        _rgbInfo.GenerateMipMaps = true;

        ShowMessage("画像読み込みの準備が整いました。URLを入力してください。");
    }

    /// <summary>
    /// URL 入力後のボタン押下などで呼び出すメソッド。
    /// 入力された URL を同期し、画像のダウンロードを開始します。
    /// </summary>
    public void OnSubmitUrl()
    {
        if (urlInputField == null)
        {
            ShowMessage("URL入力欄が設定されていません。管理者に連絡してください。");
            return;
        }

        VRCUrl url = urlInputField.GetUrl();
        if (url == null || string.IsNullOrEmpty(url.ToString()))
        {
            ShowMessage("有効なURLが入力されていません。");
            return;
        }

        // 入力後、URL入力欄をクリア（空の VRCUrl を設定）
        urlInputField.SetUrl(VRCUrl.Empty);

        // 自分をオーナーに設定してから同期する
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        syncedUrl = url;
        RequestSerialization();

        ShowMessage("画像を読み込みます。しばらくお待ちください…");
        // オーナーは直ちに画像のダウンロードを開始
        DownloadAndApplyImage(syncedUrl);
    }

    /// <summary>
    /// 他プレイヤーに同期された URL を受信した際に呼ばれるメソッド。
    /// </summary>
    public override void OnDeserialization()
    {
        ShowMessage($"新しい画像URLが同期されました: {syncedUrl}");

        // syncedUrl が空なら画像をクリア、そうでなければダウンロード開始
        if (string.IsNullOrEmpty(syncedUrl.ToString()))
        {
            ClearImageLocal();
        }
        else
        {
            DownloadAndApplyImage(syncedUrl);
        }
    }

    /// <summary>
    /// 指定された URL から画像をダウンロードし、Material に適用します。
    /// </summary>
    /// <param name="url">ダウンロード対象の画像 URL</param>
    private void DownloadAndApplyImage(VRCUrl url)
    {
        if (string.IsNullOrEmpty(url.ToString()))
        {
            ShowMessage("ダウンロードする画像のURLが空です。");
            return;
        }

        ShowMessage("画像のダウンロードを開始します…");
        _imageDownloader.DownloadImage(url, imageDisplay, _udonEventReceiver, _rgbInfo);
    }

    /// <summary>
    /// 画像のダウンロードに失敗した場合に呼ばれるコールバック。
    /// </summary>
    public override void OnImageLoadError(IVRCImageDownload result)
    {
        base.OnImageLoadError(result);
        ShowMessage("画像の読み込みに失敗しました。URLを確認してください。");
    }

    /// <summary>
    /// 画像のダウンロードに成功した場合に呼ばれるコールバック。
    /// Material にテクスチャが適用された後、画像の縦横比に合わせて Transform のサイズを再計算します。
    /// </summary>
    public override void OnImageLoadSuccess(IVRCImageDownload result)
    {
        base.OnImageLoadSuccess(result);
        ShowMessage("画像の読み込みに成功しました。");

        Texture2D tex = result.Result;
        if (tex == null)
        {
            ShowMessage("取得した画像が無効です。");
            return;
        }

        float w = tex.width;
        float h = tex.height;
        if (h <= 0.0f)
        {
            ShowMessage("画像の高さが 0 です。");
            return;
        }
        float aspect = w / h;

        // Y 軸は transformY, MAX_TRANSFORM_Y, (MAX_TRANSFORM_X / aspect) のうち最小の値にする
        float s = Mathf.Min(transformY, MAX_TRANSFORM_Y, MAX_TRANSFORM_X / aspect);
        float newY = s;
        float newX = s * aspect;

        newY = Mathf.Min(newY, MAX_TRANSFORM_Y);
        newX = Mathf.Min(newX, MAX_TRANSFORM_X);

        if (imageTransform != null)
        {
            Vector3 oldScale = imageTransform.localScale;
            imageTransform.localScale = new Vector3(newX, newY, oldScale.z);
        }
        else
        {
            ShowMessage("画像表示用オブジェクトが設定されていません。");
        }
    }

    /// <summary>
    /// uGUI から呼び出せる、全プレイヤーで画像の表示をクリアするための関数です。
    /// 自分をオーナーに設定し、同期変数を空にして、全プレイヤーで画像をクリアします。
    /// </summary>
    public void ClearImage()
    {
        // 自分をオーナーに設定
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        // 同期変数を空のURLに設定
        syncedUrl = VRCUrl.Empty;
        RequestSerialization();
        // ローカルでもクリア処理を実施
        ClearImageLocal();
        ShowMessage("画像がクリアされました。");
    }

    /// <summary>
    /// ローカルで画像表示用 Material のテクスチャをクリアする関数です。
    /// </summary>
    private void ClearImageLocal()
    {
        if (imageDisplay != null)
        {
            imageDisplay.mainTexture = null;
        }
    }

    /// <summary>
    /// ユーザー向けに画面上へメッセージを表示するメソッドです。
    /// </summary>
    /// <param name="message">表示するメッセージ</param>
    private void ShowMessage(string message)
    {
        if (debugText != null)
        {
            debugText.text = message;
        }
        Debug.Log($"[ImageLoader] {message}");
    }
}
