using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UdonSharp;
using VRC.SDKBase;

namespace nekobako {
	public class JoinLeaveLog : UdonSharpBehaviour {
		[SerializeField]
		private Text CounterText = null;

		[SerializeField]
		private Text[] LogTexts = null;

		[SerializeField]
		private string TimeFormat = "HH:mm:ss";

		[SerializeField]
		private string JoinTag = "<color=#8BC34A>[join]</color>";

		[SerializeField]
		private string LeaveTag = "<color=#F44336>[leave]</color>";

		[UdonSynced]
		private long[] Ticks = null;

		[UdonSynced]
		private bool[] Flags = null;

		[UdonSynced]
		private string[] Names = null;

		private int CurrentSecond = 0;


		private void Start() {
			if(Networking.IsOwner(this.gameObject)) {
				this.Ticks = new long[this.LogTexts.Length];
				this.Flags = new bool[this.LogTexts.Length];
				this.Names = new string[this.LogTexts.Length];
				for(int i = 0; i < this.LogTexts.Length; i++) {
					this.Ticks[i] = 0;
					this.Flags[i] = false;
					this.Names[i] = string.Empty;
				}
				this.RequestSerialization();
			}
		}

		private void Update() {
			var now = DateTime.Now;
			if(now.Second != this.CurrentSecond) {
				int count = VRCPlayerApi.GetPlayerCount();
				this.CounterText.text = $"{now.ToString(this.TimeFormat, CultureInfo.InvariantCulture)} // {count} {(count <= 1 ? "player" : "players")}";
			}
			this.CurrentSecond = now.Second;
		}

		public override void OnPlayerJoined(VRCPlayerApi player) {
			if(Networking.IsOwner(this.gameObject) && player != null && player.IsValid()) {
				this.AddLog(DateTime.UtcNow.Ticks, true, player.displayName);
				this.UpdateLog();
			}
		}

		public override void OnPlayerLeft(VRCPlayerApi player) {
			if(Networking.IsOwner(this.gameObject) && player != null && player.IsValid()) {
				this.AddLog(DateTime.UtcNow.Ticks, false, player.displayName);
				this.UpdateLog();
			}
		}

		public override void OnDeserialization() {
			this.UpdateLog();
		}

		private void AddLog(long time, bool flag, string name) {
			if(this.Ticks == null || this.Flags == null || this.Names == null) {
				return;
			}

			for(int i = 0; i < this.LogTexts.Length; i++) {
				if(string.IsNullOrEmpty(this.Names[i])) {
					this.Ticks[i] = time;
					this.Flags[i] = flag;
					this.Names[i] = name;
					this.RequestSerialization();
					return;
				}
			}

			for(int i = 0; i < this.LogTexts.Length; i++) {
				this.Ticks[i] = i < this.LogTexts.Length - 1 ? this.Ticks[i + 1] : time;
				this.Flags[i] = i < this.LogTexts.Length - 1 ? this.Flags[i + 1] : flag;
				this.Names[i] = i < this.LogTexts.Length - 1 ? this.Names[i + 1] : name;
			}
			this.RequestSerialization();
		}

		private void UpdateLog() {
			if(this.Ticks == null || this.Flags == null || this.Names == null) {
				return;
			}

			for(int i = 0; i < this.LogTexts.Length; i++) {
				if(string.IsNullOrEmpty(this.Names[i])) {
					this.LogTexts[i].text = string.Empty;
				}
				else {
					this.LogTexts[i].text = $"{new DateTime(this.Ticks[i]).ToLocalTime().ToString(this.TimeFormat, CultureInfo.InvariantCulture)} {(this.Flags[i] ? this.JoinTag : this.LeaveTag)} {this.Names[i]}";
				}
			}
		}
	}
}
