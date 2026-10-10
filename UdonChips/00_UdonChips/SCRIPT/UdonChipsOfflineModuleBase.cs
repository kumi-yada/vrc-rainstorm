
using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace UCS
{
	public class UdonChipsOfflineModuleBase : UdonSharpBehaviour
	{
		/// <summary>
		/// ロード直後の処理
		/// </summary>
		/// <param name="offline"></param>
		public virtual void OnPostLoadUdonChips(UdonChipsOffline offline)
		{

		}

		/// <summary>
		/// ロード後のOfflineModuleの処理がすべて終了した後の処理
		/// 
		/// </summary>
		/// <param name="offline"></param>
		public virtual void OnLoadCompleteUdonChips(UdonChipsOffline offline)
		{

		}

		/// <summary>
		/// セーブ直前の処理
		/// </summary>
		/// <param name="offline"></param>
		public virtual void OnPreSaveUdonChips(UdonChipsOffline offline)
		{

		}

		/// <summary>
		/// セーブ直後の処理
		/// </summary>
		/// <param name="offline"></param>
		public virtual void OnPostSaveUdonChips(UdonChipsOffline offline)
		{

		}
	}
}
