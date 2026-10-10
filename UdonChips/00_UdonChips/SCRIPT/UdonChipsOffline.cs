
using System;
using UdonSharp;
using UnityEngine;
#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
using System.Linq;
#endif

namespace UCS
{
	public class UdonChipsOffline : UdonSharpBehaviour
	{
		public UdonChipsOfflineModuleBase[] modules;

		[HideInInspector]
		public DateTime savedTime;
		[HideInInspector]
		public DateTime loadedTime;
		[HideInInspector]
		public float loadedMoney;

		[Header("Debug Settings / デバッグ設定")]
		public bool debugLog = false;

		public void OnPostLoad(UdonChips udonChips, float loadedMoney, DateTime saveTime, DateTime loadTime)
		{
			this.savedTime = saveTime;
			this.loadedTime = loadTime;
			this.loadedMoney = loadedMoney;

			if (debugLog)
			{
				float timePassed = (float)(loadTime - saveTime).TotalSeconds;
				Debug.Log($"UdonChipsOffline.OnPostLoad loadedMoney:{loadedMoney}, saveTime:{saveTime}, loadTime:{loadTime} timePassed:{timePassed}");
			}

			for (int i = 0; i < modules.Length; ++i)
			{
				if (modules[i] != null)
					modules[i].OnPostLoadUdonChips(this);
			}

			if (debugLog)
			{
				Debug.Log($"UdonChipsOffline.OnPostLoad money:{loadedMoney} -> {this.loadedMoney}");
			}

			udonChips.money = this.loadedMoney;
		}

		public void OnLoadComplete(UdonChips udonChips)
		{
			this.loadedMoney = udonChips.money;
			for (int i = 0; i < modules.Length; ++i)
			{
				if (modules[i] != null)
					modules[i].OnLoadCompleteUdonChips(this);
			}
		}

		public void OnPreSave(UdonChips udonChips)
		{
			// モジュールの逆順で実行
			for (int i = modules.Length - 1; i >= 0; --i)
			{
				if (modules[i] != null)
					modules[i].OnPreSaveUdonChips(this);
			}

			if (debugLog)
			{
				Debug.Log($"UdonChipsOffline.OnPreSave money:{udonChips.money}");
			}
		}

		public void OnPostSave(UdonChips udonChips)
		{
			// モジュールの逆順で実行
			for (int i = modules.Length - 1; i >= 0; --i)
			{
				if (modules[i] != null)
					modules[i].OnPostSaveUdonChips(this);
			}

			if (debugLog)
			{
				Debug.Log($"UdonChipsOffline.OnPostSave money:{udonChips.money}");
			}
		}

#if !COMPILER_UDONSHARP && UNITY_EDITOR
		[ContextMenu("Udon Chips/Search Modules")]
		public void SearchModules()
		{
			var tempModules = FindObjectsByType<UdonChipsOfflineModuleBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			modules = modules.Union(tempModules).ToArray();
		}

		[CustomEditor(typeof(UdonChipsOffline))]
		public class Inspector : Editor
		{
			public override void OnInspectorGUI()
			{
				base.OnInspectorGUI();

				GUILayout.Space(EditorGUIUtility.singleLineHeight);

				var udonChipsOffline = target as UdonChipsOffline;

				if (GUILayout.Button("Search Offline Modules", GUILayout.Height(EditorGUIUtility.singleLineHeight * 3f)))
				{
					Undo.RecordObject(udonChipsOffline, "Search Modules");
					udonChipsOffline.SearchModules();
					EditorUtility.SetDirty(udonChipsOffline);
				}
			}
		}
#endif
	}
}
