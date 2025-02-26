﻿using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViveportLibrary
{
    public class ViveportLibrarySettings : ObservableObject
    {
        private bool importHeadsetsAsPlatforms = true;
        private bool tagSubscriptionGames = false;
        private string subscriptionTagName = "Subscription";

        public bool ImportHeadsetsAsPlatforms { get => importHeadsetsAsPlatforms; set => SetValue(ref importHeadsetsAsPlatforms, value); }
        public bool TagSubscriptionGames { get => tagSubscriptionGames; set => SetValue(ref tagSubscriptionGames, value); }
        public string SubscriptionTagName { get => subscriptionTagName; set => SetValue(ref subscriptionTagName, value); }
        public bool ImportInputMethodsAsFeatures { get; set; } = false;
    }

    public class ViveportLibrarySettingsViewModel : PluginSettingsViewModel<ViveportLibrarySettings, ViveportLibrary>
    {
        public ViveportLibrarySettingsViewModel(ViveportLibrary plugin) : base(plugin, plugin.PlayniteApi)
        {
            // Load saved settings.
            Settings = LoadSavedSettings() ?? new ViveportLibrarySettings();
        }

        public RelayCommand<object> SetSubscriptionTagsCommand
        {
            get => new RelayCommand<object>(a =>
            {
                Plugin.SetSubscriptionTags();
            });
        }
    }
}