using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDKBase.Editor.Attributes;
using VRC.Udon;

/// <summary>
/// SkyboxSetterの有効/無効を制御するアクティベーター。
/// このGameObjectのアクティブ状態に応じて、SkyboxSetterの設定適用/復元を行います。
/// </summary>
public class SkyboxSetter_Activator : UdonSharpBehaviour
{
    #region フィールド

    [HelpBox("JP:\nこのオブジェクトがアクティブになると、指定したSkyboxSetterの設定を適用します。非アクティブになると元の設定に戻します。\nエディタ上では反映されません。\n\nEN:\nWhen this object is activated, it applies the settings of the specified SkyboxSetter. When deactivated, it restores the original settings.\nThis does not reflect in the editor.", HelpBoxAttribute.MessageType.Info)]
    [Header("------------------System----------------------")]
    [Tooltip("制御対象のSkyboxSetter")]
    [SerializeField] private SkyboxSetter skyboxSetter;

    #endregion

    #region Unity イベント

    /// <summary>
    /// アクティブ化時の処理。SkyboxSetterのカスタム設定を適用します。
    /// </summary>
    void OnEnable()
    {
        if (skyboxSetter != null)
        {
            skyboxSetter.ApplySettings();
        }
    }

    /// <summary>
    /// 非アクティブ化時の処理。SkyboxSetterをオリジナル設定に復元します。
    /// </summary>
    void OnDisable()
    {
        if (skyboxSetter != null)
        {
            skyboxSetter.RestoreSettings();
        }
    }

    #endregion
}
