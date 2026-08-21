using UnityEngine;

namespace RBS.SleepKit2.Script
{
    /// <summary>
    /// マーカークラス：シーン内の PenManagerButtonGenerator の設定値を保持します。
    /// このコンポーネントをシーン内に配置して、以下の設定を行ってください：
    ///  - buttonContainer：ボタンを配置するコンテナ（例：Canvas 内の RectTransform）
    ///  - buttonPrefab：ボタンプレハブ（例："Assets/Prefabs/PenButton.prefab"）
    ///  - teleportTarget：テレポート先の Transform
    ///  - gradientTextureResolution：グラデーションテクスチャの横解像度（例：256）
    ///  - objectToDisable：QvPen が存在しない場合、このオブジェクトを無効化し、存在する場合は有効化します。
    /// </summary>
    [ExecuteAlways]
    public class PenManagerButtonGeneratorSettings : MonoBehaviour
    {
        public Transform buttonContainer;
        public GameObject buttonPrefab;
        public Transform teleportTarget;
        public int gradientTextureResolution = 256;

        [Tooltip(
            "QvPen がプロジェクトまたはシーン内に存在しない場合、このオブジェクトを無効化し、存在する場合は有効化します。"
        )]
        public GameObject objectToDisable;
    }
}
