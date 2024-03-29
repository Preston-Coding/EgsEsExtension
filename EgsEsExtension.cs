﻿using System;
using System.Collections.Generic;
using System.Linq;
using Eleon.Modding;
using EmpyrionScripting;
using EmpyrionScripting.Interface;
using System.IO;
using System.Text;
using EmpyrionScripting.CustomHelpers;
using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;

/* ##### Changelog #####
    Remind to change version number in Settings class! :)
    2020-08-21: 1.0.0   -> Initial version
    2020-08-24: 1.0.1   -> fixed:   CpuInfHll acces memory every cycle even if no change happens
    2020-08-24: 1.0.2   -> fixed:   headline outputted multiple times depending on existing cpu count
    2020-08-24: 1.0.3   -> fixed:   CpuInfBox ignores SafetyStock containers
    2020-08-24: 1.0.4   -> adding:  display of version number
    2020-08-28: 1.0.5   -> fixed:   CpuInfHll overloads @huge structures -> Calculations split @huge structures implemented
    2020-08-30: 1.0.6   -> fixed:   a few little display issues
    2020-08-30: 1.0.7   -> adding:  ItemStructureTree.ecf - ItemGroup "Armor" and "DecosMulti" added + some missing item id's added
    2020-09-01: 1.0.8   -> fixed:   generic device state displays sometimes overlapping with next display column
    2020-09-01: 1.0.9   -> adding:  CpuCvrSrt - information display now shows additional new information about resupply progress
    2020-09-01: 1.0.10  -> fixed:   ItemStructureTree.ecf some missing item id's added
    2020-09-02: 1.0.11  -> fixed:   CpuCvrFll not taking different requests of the same item into account
    2021-06-20: 1.0.12  -> fixed:   TreeFileParser now ignores whitespaces before/after item ids / ItemStructureTree.ecf updated
    2022-03-15: 1.0.13  -> fixed:   Recompile after Mod-Dll-Fix
    2022-10-03: 1.0.14  -> fixed:   Recompile after Mod-Dll-Fix
    2023-04-04: 1.0.15  -> changed: CpuCvrSrt and CpuCvrFll: SafetyStockEquip tag removed, function fully transfered to generic SafetyStock tags
                        -> fixed:   CpuCvrSrt not listing missing items from SafetyStock containers
                        -> fixed:   Missing item count calculation summarizes wrong on container names with more then one physical container
                        -> added:   ItemStructureTree.ecf some missing item id's from newer items added
                        -> fixed:   CpuCvrFll shows the wrong localized name for the wireless connection block
    2023-04-05: 1.0.16  -> fixed:   Settings table lines without values terminate recognition of following keys
    2023-04-05: 1.0.17  -> changed: CpuInfBay: information overkill per vessel reduced to essential, sizeclass added
    2023-04-08: 1.0.18  -> changed: CpuCvrSrt now only uses SafetyStock Container to fill the tanks, not all containers (performance issue on structures with much containers)
    2023-04-08: 1.0.19  -> changed: CpuInfS replaced structure damage bar with shield level bar
    2023-04-08: 1.0.20  -> added:   CpuInfL added shield level bar and power level bar
    2023-04-08: 1.0.21  -> fixed:   CpuInfL and CpuInfS shows shield level bar in red at most levels
    2023-04-09: 1.0.22  -> fixed:   CsRoot.Move no longer needs max value per container -> removed pre divider
    2023-04-30: 1.0.23  -> added:   The font size parameter of all Cpu's now considers floating point values
*/

namespace EgsEsExtension
{
    namespace Scripts
    {
        using Locales = Locales.Locales;
        using DisplayViewManager = DisplayViewManager.DisplayViewManager;
        using GenericMethods = GenericMethods.GenericMethods;
        using ItemGroups = ItemGroups.ItemGroups;
        using CargoManagementTags = SettingsDataManager.CargoManagementTags;
        using PersistentDataStorage = PersistentDataStorage.PersistentDataStorage;
        using CargoTransferManager = CargoTransferManager.CargoTransferManager;
        using Settings = Settings.Settings;
        public static class CpuInfCpu
        {
            private const String sProcessorName = "CpuInfCpu*";
            private const String sProcessorTag = "Cpu*";
            private const String sHeartBeatPartialErrorTag = "Exception";
            private static readonly int iHeartBeatMaxFailCount = Settings.GetValue<int>(Settings.Key.TickCount_CpuInfCpu_FailsToNotify);
            public class RegisteredCpuDataSet
            {
                public String HeartBeatData { get; set; } = "";
                public int HeartBeatFailCount { get; set; } = 0;
                public String CpuName { get; private set; } = "";
                public String ConnectedDisplayName { get; private set; } = "";
                public String OutputLine { get; set; } = "";
                public RegisteredCpuDataSet(String sHeartBeatInit, String[] cpuArgs, String sInitText)
                {
                    HeartBeatData = sHeartBeatInit;
                    OutputLine = sInitText;
                    if (cpuArgs?.Count() >= 1) { CpuName = cpuArgs[0]; }
                    if (cpuArgs?.Count() >= 2) { ConnectedDisplayName = cpuArgs[1]; }
                }
            }
            private class AttachedCpuDataSet
            {
                public IBlockData Device { get; }
                public ILcd Lcd { get; }
                public bool IsRegistered { get; set; } = false;
                public String CpuName { get; private set; } = "";
                public String ConnectedDisplayName { get; private set; } = "";
                public AttachedCpuDataSet(IBlockData device, ILcd lcd, String[] cpuArgs)
                {
                    Device = device;
                    Lcd = lcd;
                    if (cpuArgs?.Count() >= 1) { CpuName = cpuArgs[0]; }
                    if (cpuArgs?.Count() >= 2) { ConnectedDisplayName = cpuArgs[1]; }
                }
            }
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                PersistentDataStorage.CheckDataReset(root, lng);
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_StatCpu));
                    // Settings load
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                    // gather attached and valid cpu devices
                    Dictionary<VectorInt3, AttachedCpuDataSet> attachedCpuList = new Dictionary<VectorInt3, AttachedCpuDataSet>();
                    IBlockData[] attachedCpuDevices = CsRoot.Devices(E.S, sProcessorTag).Where(device => !device.CustomName.Equals(sProcessorName)).ToArray();
                    attachedCpuDevices.ForEach(device =>
                    {
                        ILcd lcd = CsRoot.GetDevices<ILcd>(device).FirstOrDefault();
                        if (lcd != null)
                        {
                            attachedCpuList.Add(device.Position, new AttachedCpuDataSet(device, lcd, GenericMethods.SplitArguments(device.CustomName)));
                        }
                    });
                    // check registered cpus
                    String sActHeartBeatData;
                    String sStateBuffer;
                    String sCpuName;
                    String sConnectedDisplayName;
                    ConcurrentDictionary<VectorInt3, RegisteredCpuDataSet> registeredCpusList = PersistentDataStorage.GetRegisteredCpusList(E);
                    RegisteredCpuDataSet registeredCpuData;
                    AttachedCpuDataSet attachedCpuData;
                    registeredCpusList.ForEach(registeredCpuEntry =>
                    {
                        registeredCpuData = registeredCpuEntry.Value;
                        registeredCpuData.HeartBeatFailCount++;
                        if (attachedCpuList.TryGetValue(registeredCpuEntry.Key, out attachedCpuData))
                        {
                            attachedCpuData.IsRegistered = true;
                            sActHeartBeatData = attachedCpuData.Lcd.GetText();
                            if (!registeredCpuData.HeartBeatData.Equals(sActHeartBeatData))
                            {
                                registeredCpuData.HeartBeatFailCount = 0;
                                registeredCpuData.HeartBeatData = sActHeartBeatData;
                            }
                            sCpuName = attachedCpuData.CpuName;
                            sConnectedDisplayName = attachedCpuData.ConnectedDisplayName;
                        }
                        else
                        {
                            sCpuName = registeredCpuData.CpuName;
                            sConnectedDisplayName = registeredCpuData.ConnectedDisplayName;
                        }
                        if (registeredCpuData.HeartBeatFailCount > iHeartBeatMaxFailCount)
                        {
                            sStateBuffer = String.Format("{0}{1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Critical),
                                Locales.GetValue(lng, Locales.Key.Text_StatCpu_State_FullFail));
                        }
                        else if (registeredCpuData.HeartBeatData.Contains(sHeartBeatPartialErrorTag))
                        {
                            sStateBuffer = String.Format("{0}{1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Warning),
                            Locales.GetValue(lng, Locales.Key.Text_StatCpu_State_PartialFail));
                        }
                        else
                        {
                            sStateBuffer = String.Format("{0}{1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Fine),
                                Locales.GetValue(lng, Locales.Key.Text_StatCpu_State_NoFail));
                        }
                        registeredCpuData.OutputLine = String.Format("{0} - {1} / {2}: {3}", Settings.GetValue<String>(Settings.Key.DisplayColor_Default),
                            sCpuName, sConnectedDisplayName, sStateBuffer);
                    });
                    // register unregistered cpus
                    attachedCpuList.Where(attachedCpuEntry => !attachedCpuEntry.Value.IsRegistered).ForEach(attachedCpuEntry =>
                    {
                        attachedCpuData = attachedCpuEntry.Value;
                        registeredCpuData = new RegisteredCpuDataSet(attachedCpuData.Lcd.GetText(), GenericMethods.SplitArguments(attachedCpuData.Device.CustomName),
                            String.Format("{0}{1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Default), Locales.GetValue(lng, Locales.Key.Text_StatCpu_State_Init)));
                        registeredCpusList.TryAdd(attachedCpuEntry.Key, registeredCpuData);
                    });
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                    // draw info panel
                    displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_StatCpu_Table_States),
                        registeredCpusList.Select(cpu => cpu.Value.OutputLine).ToArray());
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuInfDev
        {
            private const String sProcessorName = "CpuInfDev*";
            private static readonly double dDamageWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Damage_Warning);
            private static readonly double dDamageCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Damage_Critical);
            public class RegisteredDeviceDataSet
            {
                public String DeviceName { get; private set; } = "";
                public double DamageLevel { get; set; } = 0;
                public String OutputLine { get; set; } = "";
                public RegisteredDeviceDataSet(String sDeviceName)
                {
                    DeviceName = sDeviceName;
                }
            }
            private class AttachedDeviceDataSet
            {
                public bool IsRegistered { get; set; } = false;
                public String DeviceName { get; private set; }
                public double DamageLevel { get; private set; }
                public AttachedDeviceDataSet(IBlockData device)
                {
                    DeviceName = device.CustomName;
                    DamageLevel = (device.HitPoints == 0 ? 0 : ((double)device.Damage / (double)device.HitPoints));
                }
            }
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                PersistentDataStorage.CheckDataReset(root, lng);
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_StatDev));
                    // Settings load
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                    // gather attached devices
                    Dictionary<VectorInt3, AttachedDeviceDataSet> attachedDeviceList = CsRoot.Devices(E.S, "*")
                        .ToDictionary(device => device.Position, device => new AttachedDeviceDataSet(device));
                    // check registered devices
                    String sStateBuffer;
                    String sDeviceName;
                    double dDamageLevel;
                    ConcurrentDictionary<VectorInt3, RegisteredDeviceDataSet> registeredDevicesList = PersistentDataStorage.GetRegisteredDevicesList(E);
                    RegisteredDeviceDataSet registeredDeviceData;
                    registeredDevicesList.ForEach(registeredDeviceEntry =>
                    {
                        registeredDeviceData = registeredDeviceEntry.Value;
                        if (attachedDeviceList.TryGetValue(registeredDeviceEntry.Key, out AttachedDeviceDataSet attachedDeviceData))
                        {
                            attachedDeviceData.IsRegistered = true;
                            sDeviceName = attachedDeviceData.DeviceName;
                            dDamageLevel = attachedDeviceData.DamageLevel;
                        }
                        else
                        {
                            sDeviceName = registeredDeviceData.DeviceName;
                            dDamageLevel = 1;
                        }
                        if (dDamageLevel >= 1.0)
                        {
                            sStateBuffer = String.Format("{0}{1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Critical),
                                Locales.GetValue(lng, Locales.Key.Text_StatDev_State_FullFail));
                        }
                        else if (dDamageLevel >= dDamageCriticalLevel)
                        {
                            sStateBuffer = String.Format("{0}{1:P1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Critical), dDamageLevel);
                        }
                        else if (dDamageLevel >= dDamageWarningLevel)
                        {
                            sStateBuffer = String.Format("{0}{1:P1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Warning), dDamageLevel);
                        }
                        else
                        {
                            sStateBuffer = String.Format("{0}{1:P1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Fine), dDamageLevel);
                        }
                        registeredDeviceData.OutputLine = String.Format("{0} - {1}: {2}", Settings.GetValue<String>(Settings.Key.DisplayColor_Default), sDeviceName, sStateBuffer);
                        registeredDeviceData.DamageLevel = dDamageLevel;
                    });
                    // register unregistered devices
                    attachedDeviceList.Where(attachedDeviceEntry => !attachedDeviceEntry.Value.IsRegistered).ForEach(attachedDeviceEntry =>
                    {
                        registeredDevicesList.TryAdd(attachedDeviceEntry.Key, new RegisteredDeviceDataSet(attachedDeviceEntry.Value.DeviceName));
                    });
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                    // draw info panel ordered by damage
                    String[] outputLines = registeredDevicesList.Where(device => device.Value.DamageLevel > 0).OrderByDescending(device => device.Value.DamageLevel)
                        .Select(device => device.Value.OutputLine).ToArray();
                    if (outputLines.Count() > 0)
                    {
                        displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_StatDev_Table_DamagedDevices), outputLines);
                    }
                    else
                    {
                        displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_StatDev_Table_DamagedDevices),
                            Locales.GetValue(lng, Locales.Key.Text_StatDev_State_NoFail));
                    }
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuInfBox
        {
            private const String sProcessorName = "CpuInfBox*";
            private static readonly int iBarSegmentCount = Settings.GetValue<int>(Settings.Key.TextFormat_BarSegmentCount);
            private static readonly double dBoxFillWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxFill_Warning);
            private static readonly double dBoxFillCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxFill_Critical);
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_StatBox));
                    // Settings load
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    SettingsDataManager.SettingsDataManager settings = new SettingsDataManager.SettingsDataManager(CsRoot, E, lng);
                    if (settings.ReadInSettingsTable(Locales.GetValue(lng, Locales.Key.SettingsTableDeviceName)))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                        // compute cargo box level bars
                        List<KeyValuePair<String, String>> containerList = settings.GetSettingsTable<String>(CargoManagementTags.ContainerTagsHeadlineTag);
                        containerList.AddRange(settings.GetSafetyStockContainers());
                        double dLevel;
                        containerList.GroupBy(grp => grp.Value).Select(grp => grp.First()).ForEach(container =>
                        {
                            dLevel = GenericMethods.ComputeContainerUsage(CsRoot, E, container.Value);
                            displayManager.AddBar(container.Value, dLevel, false, iBarSegmentCount, dBoxFillWarningLevel, dBoxFillCriticalLevel);
                        });
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                    }
                    else
                    {
                        String sText = String.Format("{0}{1} '{2}'", Environment.NewLine,
                            Locales.GetValue(lng, Locales.Key.Text_ErrorMessage_SettingsTableMissing),
                            Locales.GetValue(lng, Locales.Key.SettingsTableDeviceName));
                        displayManager.AddLogEntry(sText);
                        displayManager.AddPlainText(sText);
                    }
                    // draw info panel
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                    // error printing
                    List<KeyValuePair<String, String>> errorsTable = settings.GetFaultyParameters();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_FaultyParameter), errorsTable);
                    }
                    errorsTable = settings.GetUnknownBoxes();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_UnknownBoxes), errorsTable);
                    }
                    errorsTable = settings.GetUnknownItems();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_UnknownItems), errorsTable);
                    }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuInfWpn
        {
            private const String sProcessorName = "CpuInfWpn*";
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                PersistentDataStorage.CheckDataReset(root, lng);
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_StatWpn));
                    // Settings load
                    String sItemStructureFile = Settings.GetValue<String>(Settings.Key.FileName_ItemStructureTree);
                    if (!ItemGroups.Init(root, sItemStructureFile)) { 
                        displayManager.AddLogEntry(String.Format("- {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoItemStructureFile), sItemStructureFile)); 
                    }
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                    // gather weapon devices
                    EntityType type = E.EntityType;
                    String sTurretSymbol = Locales.GetValue(lng, Locales.Key.Symbol_StatWpn_TurretState);
                    String sWeaponSymbol = Locales.GetValue(lng, Locales.Key.Symbol_StatWpn_WeaponState);
                    if (type == EntityType.HV || type == EntityType.SV)
                    {
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_WeaponMinigunS), sWeaponSymbol, "", null, true, ItemGroups.WeaponsMinigunS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_WeaponRailgunS), sWeaponSymbol, "", null, true, ItemGroups.WeaponsRailgunS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_WeaponPlasmaS), sWeaponSymbol, "", null, true, ItemGroups.WeaponsPlasmaS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_WeaponLaserS), sWeaponSymbol, "", null, true, ItemGroups.WeaponsLaserS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_WeaponRocketS), sWeaponSymbol, "", null, true, ItemGroups.WeaponsRocketS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretMinigunS), sTurretSymbol, "", null, true, ItemGroups.TurretsMinigunS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretPlasmaS), sTurretSymbol, "", null, true, ItemGroups.TurretsPlasmaS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretRocketS), sTurretSymbol, "", null, true, ItemGroups.TurretsRocketS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretArtyS), sTurretSymbol, "", null, true, ItemGroups.TurretsArtyS);
                    }
                    else if (type == EntityType.CV || type == EntityType.BA)
                    {
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_WeaponLaserL), sWeaponSymbol, "", null, true, ItemGroups.WeaponsLaserL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_WeaponRocketL), sWeaponSymbol, "", null, true, ItemGroups.WeaponsRocketL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretSentryL), sTurretSymbol, "", null, true, ItemGroups.TurretsSentryL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretMinigunL), sTurretSymbol, "", null, true, ItemGroups.TurretsMinigunL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretCannonL), sTurretSymbol, "", null, true, ItemGroups.TurretsCannonL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretFlakL), sTurretSymbol, "", null, true, ItemGroups.TurretsFlakL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretPlasmaL), sTurretSymbol, "", null, true, ItemGroups.TurretsPlasmaL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretLaserL), sTurretSymbol, "", null, true, ItemGroups.TurretsLaserL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretRocketL), sTurretSymbol, "", null, true, ItemGroups.TurretsRocketL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatWpn_State_TurretArtyL), sTurretSymbol, "", null, true, ItemGroups.TurretsArtyL);
                    }
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                    // draw info panel
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuInfThr
        {
            private const String sProcessorName = "CpuInfThr*";
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                PersistentDataStorage.CheckDataReset(root, lng);
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_StatThr));
                    // Settings load
                    String sItemStructureFile = Settings.GetValue<String>(Settings.Key.FileName_ItemStructureTree);
                    if (!ItemGroups.Init(root, sItemStructureFile))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoItemStructureFile), sItemStructureFile));
                    }
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                    // gather weapon devices
                    EntityType type = E.EntityType;
                    if (type == EntityType.HV || type == EntityType.SV)
                    {
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatThr_State_HoverS),
                            Locales.GetValue(lng, Locales.Key.Symbol_StatThr_HoverState), "", null, true, ItemGroups.HoversS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatThr_State_ThrusterS),
                            Locales.GetValue(lng, Locales.Key.Symbol_StatThr_ThrusterSState), "", null, true, ItemGroups.ThrustersS);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatThr_State_RcsS),
                            Locales.GetValue(lng, Locales.Key.Symbol_StatThr_RcsSState), "", null, true, ItemGroups.RcssS);

                    }
                    else if (type == EntityType.CV)
                    {
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatThr_State_ThrusterL),
                            Locales.GetValue(lng, Locales.Key.Symbol_StatThr_ThrusterLState), "", null, true, ItemGroups.ThrustersL);
                        displayManager.AddDeviceStatus(Locales.GetValue(lng, Locales.Key.Headline_StatThr_State_RcsL),
                            Locales.GetValue(lng, Locales.Key.Symbol_StatThr_RcsLState), "", null, true, ItemGroups.RcssL);
                    }
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                    // draw info panel
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuInfBay
        {
            private const String sProcessorName = "CpuInfBay*";
            private class DockedVesselData
            {
                public int StructureLevel { get; private set; }
                public String VesselName { get; private set; }
                public String FactionName { get; private set; }
                public EntityType Type { get; private set; }
                public int SizeClass { get; private set; }
                public bool PowerState { get; private set; }

                public DockedVesselData(IEntityData vessel, ICsScriptFunctions CsRoot, int iStructureLevel)
                {
                    IStructureData vesselStructure = vessel.S;

                    StructureLevel = iStructureLevel;
                    VesselName = vessel.Name;
                    FactionName = vessel.Faction.ToString();
                    Type = vessel.EntityType;
                    SizeClass = vesselStructure.SizeClass;
                    PowerState = vesselStructure.IsPowerd;
                }
            }
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_StatDock));
                    // Settings load
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                    // gather vessel data
                    List<DockedVesselData> dockedVesselList = new List<DockedVesselData>();
                    AddDockedVesselToList(dockedVesselList, CsRoot, E, 0);
                    // sort and format by vessel structure
                    List<String> outputLines = new List<String>();
                    dockedVesselList.ForEach(vessel =>
                    {
                        String sVesselId = String.Format("{0}{1} / {2}", Settings.GetValue<String>(Settings.Key.DisplayColor_White),
                            vessel.VesselName, vessel.FactionName);
                        String sVesselEnergy = String.Format("{0}{1}: {2}", Settings.GetValue<String>(Settings.Key.DisplayColor_Yellow),
                            Locales.GetValue(lng, Locales.Key.Text_StatDock_ItemText_Energy),
                            vessel.PowerState == false ? Locales.GetValue(lng, Locales.Key.Text_StatDock_State_Off) : Locales.GetValue(lng, Locales.Key.Text_StatDock_State_On));
                        String sSizeClass = String.Format("{0}{1}: {2} / {3}", Settings.GetValue<String>(Settings.Key.DisplayColor_Yellow),
                            Locales.GetValue(lng, Locales.Key.Text_StatDock_ItemText_SizeClass), Locales.GetEntityTypeNameShort(lng, vessel.Type), vessel.SizeClass);
                        String sStructureIndent = new String(' ', vessel.StructureLevel);
                        outputLines.Add(String.Format("{0}- {1}, {2}, {3}", sStructureIndent, sVesselId, sVesselEnergy, sSizeClass));
                    });
                    // draw info panel
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                    if (outputLines.Count() > 0)
                    {
                        displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_StatDock_Table_DockedVessels), outputLines.ToArray());
                    }
                    else
                    {
                        displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_StatDock_Table_DockedVessels),
                            Locales.GetValue(lng, Locales.Key.Text_StatDock_State_NoVessel));
                    }
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
            private static void AddDockedVesselToList(List<DockedVesselData> dockedVesselList, ICsScriptFunctions CsRoot, IEntityData E, int iStructureLevel)
            {
                E.S.DockedE.ForEach(vessel =>
                {
                    dockedVesselList.Add(new DockedVesselData(vessel, CsRoot, iStructureLevel));
                    AddDockedVesselToList(dockedVesselList, CsRoot, vessel, (iStructureLevel + 1));
                });
            }
        }
        public static class CpuInfHll
        {
            // controls
            private const String sProcessorName = "CpuInfHll*";
            private const int iVirtualYCorrection = 128;
            private static readonly int iLayersPerTick = Settings.GetValue<int>(Settings.Key.TickCount_CpuInfHll_LayersPerTick);
            public class RegisteredStructureDataSet
            {
                public double?[][][] StructDamageData { get; set; } = null;
                public ComputingSteps ComputingStep { get; set; } = 0;
                public int NextLayerToStartFrom { get; set; } = 0;
                public int DisplayToDraw { get; set; } = 0;
                public bool IsStructureInitilized { get; set; } = false;
            }
            public enum ComputingSteps
            {
                eStoragedInitialising,
                eStructureCalculation,
                eViewDraw
            }
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                //Script Content Data
                PersistentDataStorage.CheckDataReset(root, lng);
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_StatHll));
                    // Settings load
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                    // view calculation state machine
                    int iPosX;
                    int iPosY;
                    int iPosZ;
                    VectorInt3 structMin = E.S.MinPos;
                    VectorInt3 structMax = E.S.MaxPos;
                    RegisteredStructureDataSet storedStructureData = PersistentDataStorage.GetRegisteredStructureDataSet(E);
                    double?[][][] dStructDamageData = storedStructureData.StructDamageData;
                    displayManager.AddLogEntry(String.Format("- {0}: {1} / {2}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedStepXFromY), 
                        ((int)storedStructureData.ComputingStep) + 1, Enum.GetNames(typeof(ComputingSteps)).Length));
                    switch (storedStructureData.ComputingStep)
                    {
                        case ComputingSteps.eStoragedInitialising:
                            // generate data array size
                            int iSizeX = structMax.x - structMin.x + 1;
                            int iSizeY = structMax.y - structMin.y + 1;
                            int iSizeZ = structMax.z - structMin.z + 1;
                            dStructDamageData = new double?[iSizeX][][];
                            for (iPosX = 0; iPosX < dStructDamageData.Length; iPosX++)
                            {
                                dStructDamageData[iPosX] = new double?[iSizeY][];
                                for (iPosY = 0; iPosY < dStructDamageData[iPosX].Length; iPosY++)
                                {
                                    dStructDamageData[iPosX][iPosY] = new double?[iSizeZ];
                                }
                            }
                            storedStructureData.StructDamageData = dStructDamageData;
                            displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataReadIn)));
                            storedStructureData.ComputingStep = ComputingSteps.eStructureCalculation;
                            break;
                        case ComputingSteps.eStructureCalculation:
                            // refresh structure damage data in array
                            int iStaggeredXStart = storedStructureData.NextLayerToStartFrom;
                            int iStaggeredXEnd = iStaggeredXStart + iLayersPerTick;
                            bool bInitilized = storedStructureData.IsStructureInitilized;
                            for (iPosX = iStaggeredXStart; iPosX < iStaggeredXEnd && iPosX < dStructDamageData.Length; iPosX++)
                            {
                                for (iPosY = 0; iPosY < dStructDamageData[0].Length; iPosY++)
                                {
                                    for (iPosZ = 0; iPosZ < dStructDamageData[0][0].Length; iPosZ++)
                                    {
                                        IBlockData block = CsRoot.Block(E.S, iPosX + structMin.x, iPosY + structMin.y + iVirtualYCorrection, iPosZ + structMin.z);
                                        double? dValue = dStructDamageData[iPosX][iPosY][iPosZ];
                                        if (block != null && block.Id > 0)
                                        {
                                            dStructDamageData[iPosX][iPosY][iPosZ] = (double)block.Damage / block.HitPoints;
                                        }
                                        else if (dValue.HasValue)
                                        {
                                            dStructDamageData[iPosX][iPosY][iPosZ] = 1.0;
                                        }
                                        else if (!bInitilized)
                                        {
                                            dStructDamageData[iPosX][iPosY][iPosZ] = null;
                                        }
                                    }
                                }
                            }
                            if (iPosX >= dStructDamageData.Length)
                            {
                                // convert damaga data to geometric views
                                storedStructureData.NextLayerToStartFrom = 0;
                                storedStructureData.IsStructureInitilized = true;
                                displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                                storedStructureData.ComputingStep = ComputingSteps.eViewDraw;
                            }
                            else
                            {
                                storedStructureData.NextLayerToStartFrom = iPosX;
                                displayManager.AddLogEntry(String.Format("- {0}: {1} / {2}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedComputingXFromY), iPosX, dStructDamageData.Length));
                            }
                            break;
                        case ComputingSteps.eViewDraw:
                            // convert damaga data to geometric views
                            int iMaxDisplayGroupCount = displayManager.GetMaxInfoDisplayGroupCount();
                            int iDisplayToDraw = storedStructureData.DisplayToDraw;
                            displayManager.AddStructureView(dStructDamageData, DisplayViewManager.StructureViews.eTopView);
                            displayManager.AddStructureView(dStructDamageData, DisplayViewManager.StructureViews.eDeckView);
                            if (displayManager.DrawFormattedInfoView(iDisplayToDraw))
                            { 
                                displayManager.AddLogEntry(String.Format("- {0}: {1} / {2}", 
                                    Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw), (iDisplayToDraw + 1), iMaxDisplayGroupCount)); 
                            }
                            else
                            { 
                                displayManager.AddLogEntry(String.Format("- {0}: {1} / {2}", 
                                    Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound), (iDisplayToDraw + 1), iMaxDisplayGroupCount)); 
                            }
                            if ((iDisplayToDraw + 1) < iMaxDisplayGroupCount) { 
                                storedStructureData.DisplayToDraw++;
                            } 
                            else 
                            { 
                                storedStructureData.DisplayToDraw = 0;
                                storedStructureData.ComputingStep = ComputingSteps.eStructureCalculation;
                            }
                            break;
                        default: break;
                    }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuInfL
        {
            private const String sProcessorName = "CpuInfL*";
            private static readonly int iBarSegmentCount = Settings.GetValue<int>(Settings.Key.TextFormat_BarSegmentCount);
            private static readonly double dFluidsWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_TanksEmpty_Warning);
            private static readonly double dFluidsCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_TanksEmpty_Critical);
            private static readonly double dPowerWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Power_Warning);
            private static readonly double dPowerCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Power_Critical);
            private static readonly double dShieldWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Shield_Warning);
            private static readonly double dShieldCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Shield_Critical);
            private static readonly double dDamageWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Damage_Warning);
            private static readonly double dDamageCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Damage_Critical);
            private static readonly double dBoxFillWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxFill_Warning);
            private static readonly double dBoxFillCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxFill_Critical);
            private static readonly double dBoxEmptyWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxEmpty_Warning);
            private static readonly double dBoxEmptyCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxEmpty_Critical);
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                PersistentDataStorage.CheckDataReset(root, lng);
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                IPlayfieldData P = root.P;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_StatusL));
                    // Settings load
                    String sItemStructureFile = Settings.GetValue<String>(Settings.Key.FileName_ItemStructureTree);
                    if (!ItemGroups.Init(root, sItemStructureFile))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoItemStructureFile), sItemStructureFile));
                    }
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    SettingsDataManager.SettingsDataManager settings = new SettingsDataManager.SettingsDataManager(CsRoot, E, lng);
                    if (settings.ReadInSettingsTable(Locales.GetValue(lng, Locales.Key.SettingsTableDeviceName)))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                        // compute infos
                        String sPVPText;
                        if (P.IsPvP)
                        {
                            sPVPText = String.Format("{0}{1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Critical),
                                Locales.GetValue(lng, Locales.Key.Text_StatusS_OriginItemText_PVPActive));
                        }
                        else
                        {
                            sPVPText = String.Format("{0}{1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Fine),
                                Locales.GetValue(lng, Locales.Key.Text_StatusS_OriginItemText_PVPInactive));
                        }
                        displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_StatusL_OriginInfo),
                            String.Format(" {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_StatusL_OriginItemName_System), P.SolarSystemName),
                            String.Format(" {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_StatusL_OriginItemName_Place), P.Name),
                            String.Format(" {0}: {1} / {2}", Locales.GetValue(lng, Locales.Key.Text_StatusL_OriginItemName_Class), P.PlanetClass, P.PlayfieldType),
                            String.Format(" {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_StatusL_OriginItemName_PVP), sPVPText),
                            String.Format(" {0}:", Locales.GetValue(lng, Locales.Key.Text_StatusL_OriginItemName_SystemPosition)),
                            String.Format("  X:{0} Y:{1} Z:{2}", P.SolarSystemCoordinates.x, P.SolarSystemCoordinates.y, P.SolarSystemCoordinates.z),
                            String.Format(" {0}:", Locales.GetValue(lng, Locales.Key.Text_StatusL_OriginItemName_Position)),
                            String.Format("  X:{0:N2} Y:{1:N2} Z:{2:N2}", E.Pos.x, E.Pos.y, E.Pos.z)
                        );
                        IEntityData[] dockedVessels = E.S.DockedE;
                        displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_StatusL_DockingInfo),
                            String.Format(" {0}: {1}", Locales.GetEntityTypeNameLong(lng, EntityType.HV), dockedVessels.Count(vessel => vessel.EntityType == EntityType.HV)),
                            String.Format(" {0}: {1}", Locales.GetEntityTypeNameLong(lng, EntityType.SV), dockedVessels.Count(vessel => vessel.EntityType == EntityType.SV)),
                            String.Format(" {0}: {1}", Locales.GetEntityTypeNameLong(lng, EntityType.CV), dockedVessels.Count(vessel => vessel.EntityType == EntityType.CV))
                        );
                        if (E.EntityType != EntityType.BA)
                        {
                            displayManager.AddSimpleInfoTable(Locales.GetValue(lng,
                                Locales.Key.Headline_StatusL_PassengerInfo), String.Format(" {0}", E.S.Passengers.Count()));
                        }
                        // compute status
                        displayManager.AddDeviceStatus(
                            Locales.GetValue(lng, Locales.Key.Headline_StatusL_ShieldState),
                            Locales.GetValue(lng, Locales.Key.Symbol_StatusL_ShieldState),
                            Locales.GetValue(lng, Locales.Key.Text_ErrorMessage_StatusL_ShieldMissing),
                            ItemGroups.ShieldsS, ItemGroups.ShieldsL);
                        displayManager.AddDeviceStatus(
                            Locales.GetValue(lng, Locales.Key.Headline_StatusL_TurretState),
                            Locales.GetValue(lng, Locales.Key.Symbol_StatusL_TurretState),
                            Locales.GetValue(lng, Locales.Key.Text_ErrorMessage_StatusL_TurretMissing),
                            ItemGroups.TurretsWeaponS, ItemGroups.TurretsWeaponL);
                        displayManager.AddDeviceStatus(
                            Locales.GetValue(lng, Locales.Key.Headline_StatusL_ThrusterState),
                            Locales.GetValue(lng, Locales.Key.Symbol_StatusL_ThrusterState),
                            Locales.GetValue(lng, Locales.Key.Text_ErrorMessage_StatusL_ThrusterMissing), EntityType.BA, false,
                            ItemGroups.ThrustersS, ItemGroups.ThrustersL);
                        // compute level bars
                        double dLevel;
                        IStructureTankWrapper tank;
                        tank = E.S.FuelTank;
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusL_FuelBar),
                            tank.Content, tank.Capacity, true, iBarSegmentCount, dFluidsWarningLevel, dFluidsCriticalLevel);
                        tank = E.S.OxygenTank;
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusL_OxygenBar),
                            tank.Content, tank.Capacity, true, iBarSegmentCount, dFluidsWarningLevel, dFluidsCriticalLevel);
                        tank = E.S.PentaxidTank;
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusL_PentaxidBar),
                            tank.Content, tank.Capacity, true, iBarSegmentCount, dFluidsWarningLevel, dFluidsCriticalLevel);
                        displayManager.AddBlankLine();
                        dLevel = GenericMethods.GetPowerLevel(CsRoot, E.S);
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusL_PowerBar),
                            dLevel, 1, false, iBarSegmentCount, dPowerWarningLevel, dPowerCriticalLevel);
                        dLevel = GenericMethods.GetShieldLevel(CsRoot, E.S);
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusL_ShieldBar),
                            dLevel, 1, true, iBarSegmentCount, dShieldWarningLevel, dShieldCriticalLevel);
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusL_DamageBar),
                            E.S.DamageLevel, 1, false, 15, dDamageWarningLevel, dDamageCriticalLevel);
                        displayManager.AddBlankLine();
                        dLevel = GenericMethods.ComputeItemCompletenessLevel(CsRoot, E,
                            settings.GetParameterValue<String>(CargoManagementTags.SafetyStockAmmoHeadlineTag),
                            settings.GetSettingsTable<int>(CargoManagementTags.SafetyStockAmmoHeadlineTag), out _);
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusL_AmmoBar),
                            dLevel, true, iBarSegmentCount, dBoxEmptyWarningLevel, dBoxEmptyCriticalLevel);
                        dLevel = GenericMethods.ComputeContainerUsage(CsRoot, E, "*", true);
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusL_CargoBar),
                            dLevel, false, iBarSegmentCount, dBoxFillWarningLevel, dBoxFillCriticalLevel);
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                    }
                    else
                    {
                        String sText = String.Format("{0}{1} '{2}'", Environment.NewLine,
                            Locales.GetValue(lng, Locales.Key.Text_ErrorMessage_SettingsTableMissing),
                            Locales.GetValue(lng, Locales.Key.SettingsTableDeviceName));
                        displayManager.AddLogEntry(sText);
                        displayManager.AddPlainText(sText);
                    }
                    // draw info panel
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                    // error printing
                    List<KeyValuePair<String, String>> errorsTable = settings.GetFaultyParameters();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_FaultyParameter), errorsTable);
                    }
                    errorsTable = settings.GetUnknownBoxes();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_UnknownBoxes), errorsTable);
                    }
                    errorsTable = settings.GetUnknownItems();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_UnknownItems), errorsTable);
                    }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuInfS
        {
            private const String sProcessorName = "CpuInfS*";
            private static readonly int iBarSegmentCount = Settings.GetValue<int>(Settings.Key.TextFormat_BarSegmentCount);
            private static readonly double dFluidsWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_TanksEmpty_Warning);
            private static readonly double dFluidsCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_TanksEmpty_Critical);
            private static readonly double dShieldWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Shield_Warning);
            private static readonly double dShieldCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_Shield_Critical);
            private static readonly double dBoxEmptyWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxEmpty_Warning);
            private static readonly double dBoxEmptyCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxEmpty_Critical);
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                IPlayfieldData P = root.P;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_StatusS));
                    // Settings load
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    SettingsDataManager.SettingsDataManager settings = new SettingsDataManager.SettingsDataManager(CsRoot, E, lng);
                    if (settings.ReadInSettingsTable(Locales.GetValue(lng, Locales.Key.SettingsTableDeviceName)))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                        // compute infos
                        String sPVPText;
                        if (P.IsPvP) { 
                            sPVPText = String.Format("{0}{1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Critical), 
                                Locales.GetValue(lng, Locales.Key.Text_StatusS_OriginItemText_PVPActive)); }
                        else { 
                            sPVPText = String.Format("{0}{1}", Settings.GetValue<String>(Settings.Key.DisplayColor_Fine), 
                                Locales.GetValue(lng, Locales.Key.Text_StatusS_OriginItemText_PVPInactive)); 
                        }
                        displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_StatusS_OriginInfo),
                            String.Format(" {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_StatusS_OriginItemName_Place), P.Name),
                            String.Format(" {0}: {1} / {2}", Locales.GetValue(lng, Locales.Key.Text_StatusS_OriginItemName_Class), P.PlanetClass, P.PlayfieldType),
                            String.Format(" {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_StatusS_OriginItemName_PVP), sPVPText),
                            String.Format(" {0}: {1:N1} / {2:N1} / {3:N1}", Locales.GetValue(lng, Locales.Key.Text_StatusS_OriginItemName_Position), E.Pos.x, E.Pos.y, E.Pos.z)
                        );
                        // compute level bars
                        double dLevel;
                        IStructureTankWrapper tank;
                        tank = E.S.FuelTank;
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusS_FuelBar),
                            tank.Content, tank.Capacity, true, iBarSegmentCount, dFluidsWarningLevel, dFluidsCriticalLevel);
                        tank = E.S.OxygenTank;
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusS_OxygenBar),
                            tank.Content, tank.Capacity, true, iBarSegmentCount, dFluidsWarningLevel, dFluidsCriticalLevel);
                        tank = E.S.PentaxidTank;
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusS_PentaxidBar),
                            tank.Content, tank.Capacity, true, iBarSegmentCount, dFluidsWarningLevel, dFluidsCriticalLevel);
                        dLevel = GenericMethods.GetShieldLevel(CsRoot, E.S);
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusS_ShieldBar),
                            dLevel, 1, true, iBarSegmentCount, dShieldWarningLevel, dShieldCriticalLevel);
                        dLevel = GenericMethods.ComputeItemCompletenessLevel(CsRoot, E,
                            settings.GetParameterValue<String>(CargoManagementTags.SafetyStockAmmoHeadlineTag),
                            settings.GetSettingsTable<int>(CargoManagementTags.SafetyStockAmmoHeadlineTag), out _);
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_StatusS_AmmoBar),
                            dLevel, true, iBarSegmentCount, dBoxEmptyWarningLevel, dBoxEmptyCriticalLevel);
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                    }
                    else
                    {
                        String sText = String.Format("{0}{1} '{2}'", Environment.NewLine,
                            Locales.GetValue(lng, Locales.Key.Text_ErrorMessage_SettingsTableMissing),
                            Locales.GetValue(lng, Locales.Key.SettingsTableDeviceName));
                        displayManager.AddLogEntry(sText);
                        displayManager.AddPlainText(sText);
                    }
                    // draw info panel
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                    // error printing
                    List<KeyValuePair<String, String>> errorsTable = settings.GetFaultyParameters();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_FaultyParameter), errorsTable);
                    }
                    errorsTable = settings.GetUnknownBoxes();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_UnknownBoxes), errorsTable);
                    }
                    errorsTable = settings.GetUnknownItems();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_UnknownItems), errorsTable);
                    }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuCvrSrt
        {
            private const String sProcessorName = "CpuCvrSrt*";
            private const String sBoxSeperator = ",";
            private static readonly int iBarSegmentCount = Settings.GetValue<int>(Settings.Key.TextFormat_BarSegmentCount);
            private static readonly double dBoxFillWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxFill_Warning);
            private static readonly double dBoxFillCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxFill_Critical);
            private static readonly double dBoxEmptyWarningLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxEmpty_Warning);
            private static readonly double dBoxEmptyCriticalLevel = Settings.GetValue<double>(Settings.Key.CompareLevel_BoxEmpty_Critical);
            private static readonly int iAllowedItemCountToMove = Settings.GetValue<int>(Settings.Key.TickCount_ItemsToSort);
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_CargoSorter));
                    // Settings load
                    String sItemStructureFile = Settings.GetValue<String>(Settings.Key.FileName_ItemStructureTree);
                    if (!ItemGroups.Init(root, sItemStructureFile))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoItemStructureFile), sItemStructureFile));
                    }
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    SettingsDataManager.SettingsDataManager settings = new SettingsDataManager.SettingsDataManager(CsRoot, E, lng);
                    if (settings.ReadInSettingsTable(Locales.GetValue(lng, Locales.Key.SettingsTableDeviceName)))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));

                        // refill tanks
                        List<KeyValuePair<String, String>> refillSourceList = settings.GetSafetyStockContainers(true);
                        int? iFuelLimit = settings.GetParameterValue<int?>(CargoManagementTags.FluidLevelTag_Fuel);
                        int? iOxygenLimit = settings.GetParameterValue<int?>(CargoManagementTags.FluidLevelTag_Oxygen);
                        int? iPentaxidLimit = settings.GetParameterValue<int?>(CargoManagementTags.FluidLevelTag_Pentaxid);
                        int iFilledFuel = 0;
                        int iFilledOxygen = 0;
                        int iFilledPentaxid = 0;
                        refillSourceList.ForEach(container =>
                        {
                            CsRoot.Items(E.S, container.Value).ForEach(item =>
                            {
                                if (iFuelLimit != null) { iFilledFuel += CsRoot.Fill(item, E.S, StructureTankType.Fuel, iFuelLimit).Sum(itemInfo => itemInfo.Count); }
                                if (iOxygenLimit != null) { iFilledOxygen += CsRoot.Fill(item, E.S, StructureTankType.Oxygen, iOxygenLimit).Sum(itemInfo => itemInfo.Count); }
                                if (iPentaxidLimit != null) { iFilledPentaxid += CsRoot.Fill(item, E.S, StructureTankType.Pentaxid, iPentaxidLimit).Sum(itemInfo => itemInfo.Count); }
                            });
                        });
                        if (refillSourceList.Any(container => CsRoot.Devices(E.S, container.Value).Count() > 0))
                        {
                            displayManager.AddLogEntry(String.Format("- {0}: {1} / {2} / {3}",
                                Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedFluidRefill),
                                iFilledFuel, iFilledOxygen, iFilledPentaxid));
                        }
                        else
                        {
                            displayManager.AddLogEntry(String.Format("- {0}: {1}",
                                Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedFluidRefill),
                                Locales.GetValue(lng, Locales.Key.Text_ErrorMessage_SafetyStockContainerNotFound)
                                ));
                        }
                        
                        // refilling safety stock
                        int iItemCount = 0;
                        String sSourceBox = String.Join(sBoxSeperator,
                            settings.GetParameterValue<String>(CargoManagementTags.ContainerTag_Input),
                            settings.GetParameterValue<String>(CargoManagementTags.ContainerTag_SafetySource));
                        settings.GetSafetyStockContainers().ForEach(container =>
                        {
                            settings.GetSettingsTable<int>(container).ForEach(item =>
                            {
                                if (iItemCount < iAllowedItemCountToMove)
                                {
                                    if (GenericMethods.IntelligentItemMove(CsRoot, E, E, sSourceBox, container.Value, item) > 0)
                                    {
                                        iItemCount++;
                                    }
                                }
                            });
                            displayManager.AddLogEntry(String.Format("- {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSafetyStockRefill), container.Value));
                        });

                        // inputbox cargo sorting
                        String sTargetBoxName;
                        sSourceBox = settings.GetParameterValue<String>(CargoManagementTags.ContainerTag_Input);
                        if (sSourceBox != null)
                        {
                            CsRoot.Items(E.S, sSourceBox).ForEach(item =>
                            {
                                if (iItemCount < iAllowedItemCountToMove)
                                {
                                    sTargetBoxName = settings.GetContainerNameByItemId(item.Id);
                                    if (sTargetBoxName != null)
                                    {
                                        if (CsRoot.Move(item, E.S, sTargetBoxName).Count > 0)
                                        {
                                            iItemCount++;
                                        }
                                    }
                                }
                            });
                        }
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedCargoSorting)));
                        
                        // calculate remaining cargo space
                        String sContainerNames = String.Join(",", settings.GetParameterValue<String>(CargoManagementTags.ContainerTag_Output),
                            settings.GetParameterValue<String>(CargoManagementTags.ContainerTag_SafetySource));
                        double dLevel = GenericMethods.ComputeContainerUsage(CsRoot, E, sContainerNames, true);
                        displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_CargoSorter_CargoBar),
                            dLevel, false, iBarSegmentCount, dBoxFillWarningLevel, dBoxFillCriticalLevel);
                        
                        // calculate resupply progress
                        List<KeyValuePair<string, int>> missingItems = new List<KeyValuePair<string, int>>();
                        int iContainerCount = 0;
                        dLevel = 0;
                        settings.GetSafetyStockContainers().ForEach(container => {
                            dLevel += GenericMethods.ComputeItemCompletenessLevel(CsRoot, E, container.Value,
                                settings.GetSettingsTable<int>(container), out List<KeyValuePair<string, int>> missingStockItems);
                            missingItems.AddRange(missingStockItems);
                            iContainerCount++;
                        });
                        if (iContainerCount > 0)
                        {
                            displayManager.AddBar(Locales.GetValue(lng, Locales.Key.Headline_CargoSorter_ResupplyBar),
                                dLevel / (double)iContainerCount, true, iBarSegmentCount, dBoxEmptyWarningLevel, dBoxEmptyCriticalLevel);
                        }
                        if (missingItems.Count > 0)
                        {
                            displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_CargoSorter_Table_ItemsMissing), missingItems.GroupBy(item => item.Key)
                                .Select(grp => new KeyValuePair<String, int>(grp.Key, grp.Sum(count => count.Value)))
                                .Select(item => String.Format("- {0}: {1}pcs", item.Key, item.Value)).ToArray());
                        }
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataComputation)));
                    }
                    else
                    {
                        String sText = String.Format("{0}{1} '{2}'", Environment.NewLine,
                            Locales.GetValue(lng, Locales.Key.Text_ErrorMessage_SettingsTableMissing),
                            Locales.GetValue(lng, Locales.Key.SettingsTableDeviceName));
                        displayManager.AddLogEntry(sText);
                        displayManager.AddPlainText(sText);
                    }
                    // error printing
                    List<KeyValuePair<String, String>> errorsTable = settings.GetFaultyParameters();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_FaultyParameter), errorsTable);
                    }
                    errorsTable = settings.GetUnknownBoxes();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_UnknownBoxes), errorsTable);
                    }
                    errorsTable = settings.GetUnknownItems();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_UnknownItems), errorsTable);
                        displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_UnknownItems), errorsTable
                            .Select(entry => String.Format("{0}: {1}", entry.Key, entry.Value)).ToArray());
                    }
                    // draw info panel
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuCvrFll
        {
            private const String sProcessorName = "CpuCvrFll*";
            private static readonly int iVesselProcessedPerTick = Settings.GetValue<int>(Settings.Key.TickCount_VesselsToFill);
            private static readonly int iItemMovedPerTick = Settings.GetValue<int>(Settings.Key.TickCount_ItemsToFill);
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_BoxFill));
                    // load setting
                    String sItemStructureFile = Settings.GetValue<String>(Settings.Key.FileName_ItemStructureTree);
                    if (!ItemGroups.Init(root, sItemStructureFile))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoItemStructureFile), sItemStructureFile));
                    }
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    // Service manager for vessels and data
                    CargoTransferManager cargoManager = new CargoTransferManager(CsRoot, E, lng);
                    List<String> vesselInfoTableLineList = new List<String>();
                    List<KeyValuePair<String, int>> itemRequestsList = new List<KeyValuePair<String, int>>(); // itemname and count
                    List<KeyValuePair<String, int>> missingItemList = new List<KeyValuePair<String, int>>(); // itemname and count
                    Dictionary<IEntityData, String> rejectedVesselList = new Dictionary<IEntityData, String>(); // vessel and rejectreason
                    Dictionary<IEntityData, double> remainingVesselList = new Dictionary<IEntityData, double>(); // vessel and completenesslevel
                    // test manager readyness
                    if (cargoManager.IsVesselReady(E, false, CargoManagementTags.ContainerTag_Output, CargoManagementTags.CargoOutputActiveTag, out String sError))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                        CsRoot.EntitiesByName("*").ForEach(vessel =>
                        {
                            // test receiver vessel validity
                            if (cargoManager.IsVesselValid(vessel))
                            {
                                // test receiver vessel readyness
                                if (!cargoManager.IsVesselReady(vessel, false, CargoManagementTags.ContainerTag_Input, CargoManagementTags.CargoInputActiveTag, out sError)){
                                    rejectedVesselList.Add(vessel, sError);
                                }
                            }
                        });
                        // process vessels by priority
                        int iVesselProcessedCount = 0;
                        int iItemMovedCount = 0;
                        cargoManager.GetPriorityOrderedValidRequestors().ForEach(vesselSetting =>
                        {
                            // prepare request list
                            itemRequestsList.Clear();
                            vesselSetting.GetSafetyStockContainers().ForEach(container => {
                                itemRequestsList.AddRange(vesselSetting.GetSettingsTable<int>(container));
                            });
                            // prepare measures
                            double dVesselProgressAct = 0;
                            double dVesselProgressMax = 0;
                            bool bSomeItemMoved = false;
                            int iVesselItemsCount;
                            int iAvailableItemsCount;
                            int iMissingItemsCount;
                            int iTransferItemsCount;
                            itemRequestsList.GroupBy(item => item.Key).Select(grp => new KeyValuePair<String, int>(grp.Key, grp.Sum(count => count.Value))).ForEach(item =>
                            {
                                dVesselProgressMax += item.Value;
                                // check item gab
                                IItemsData vesselItems = CsRoot.Items(vesselSetting.E.S, "*").FirstOrDefault(items => CsRoot.I18n(items.Id) == item.Key);
                                iVesselItemsCount = (vesselItems?.Count).GetValueOrDefault(0);
                                iMissingItemsCount = 0;
                                if (iVesselItemsCount < item.Value)
                                {
                                    // search available items
                                    IItemsData availableItems = CsRoot.Items(E.S, cargoManager.GetManagerContainerName()).FirstOrDefault(items => CsRoot.I18n(items.Id) == item.Key);
                                    iAvailableItemsCount = (availableItems?.Count).GetValueOrDefault(0);
                                    // calc transfer parameter
                                    iTransferItemsCount = Math.Min((item.Value - iVesselItemsCount), iAvailableItemsCount);
                                    iMissingItemsCount = item.Value - iVesselItemsCount - iTransferItemsCount;
                                    if (iTransferItemsCount > 0)
                                    {
                                        // artificial conveyor simulation
                                        if (iVesselProcessedCount < iVesselProcessedPerTick && iItemMovedCount < iItemMovedPerTick)
                                        {
                                            CsRoot.Move(availableItems, vesselSetting.E.S, cargoManager.GetRemoteContainerName(vesselSetting.E), iTransferItemsCount);
                                            iVesselItemsCount += iTransferItemsCount;
                                            bSomeItemMoved = true;
                                            iItemMovedCount++;
                                        }
                                    }
                                }
                                // calc progress
                                dVesselProgressAct += Math.Min(iVesselItemsCount, item.Value);
                                // register missing items
                                if (iMissingItemsCount > 0)
                                {
                                    missingItemList.Add(new KeyValuePair<String, int>(item.Key, iMissingItemsCount));
                                }
                            });
                            // vessel count
                            if (bSomeItemMoved)
                            {
                                iVesselProcessedCount++;
                            }
                            // register incomplete vessel
                            if (dVesselProgressAct < dVesselProgressMax)
                            {
                                remainingVesselList.Add(vesselSetting.E, dVesselProgressAct / dVesselProgressMax);
                            }
                        });
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedCargoTransfer)));
                        // compute info results
                        // remaining vessels
                        if (remainingVesselList.Count() > 0)
                        {
                            vesselInfoTableLineList.Clear();
                            remainingVesselList.ForEach(vessel =>
                            {
                                vesselInfoTableLineList.Add(String.Format(" - {0} {1} {2:P2}", vessel.Key.Name, Locales.GetValue(lng, Locales.Key.Text_BoxFill_VesselProgressText), vessel.Value));
                            });
                            displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_BoxFill_Table_VesselsRemaining), vesselInfoTableLineList.ToArray());
                        }
                        // missing items
                        if (missingItemList.Count() > 0)
                        {
                            vesselInfoTableLineList.Clear();
                            missingItemList.GroupBy(item => item.Key).Select(grp => new KeyValuePair<String, int>(grp.Key, grp.Sum(count => count.Value))).ForEach(item =>
                            {
                                vesselInfoTableLineList.Add(String.Format(" - {0}: {1}pcs", item.Key, item.Value));
                            });
                            displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_BoxFill_Table_ItemsMissing), vesselInfoTableLineList.ToArray());
                        }
                        // vessels with error
                        if (rejectedVesselList.Count() > 0)
                        {
                            vesselInfoTableLineList.Clear();
                            rejectedVesselList.ForEach(vessel =>
                            {
                                vesselInfoTableLineList.Add(String.Format(" - {0}: {1}", vessel.Key.Name, vessel.Value));
                            });
                            displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_BoxFill_Table_VesselsRejected), vesselInfoTableLineList.ToArray());
                        }
                        // standby
                        if (remainingVesselList.Count() == 0 && missingItemList.Count() == 0 && rejectedVesselList.Count() == 0)
                        {
                            displayManager.AddPlainText(String.Format("{0}{1}{2}", Environment.NewLine,
                                Settings.GetValue<String>(Settings.Key.DisplayColor_Fine), Locales.GetValue(lng, Locales.Key.Text_StandBy)));
                        }
                    }
                    else
                    {
                        displayManager.AddLogEntry(String.Format("- {0}", sError));
                        displayManager.AddPlainText(sError);
                    }
                    // draw info panel
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                    // error printing
                    List<KeyValuePair<String, String>> errorsTable = cargoManager.GetManagerSettings().GetFaultyParameters();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_FaultyParameter), errorsTable);
                    }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
        public static class CpuCvrPrg
        {
            private const String sProcessorName = "CpuCvrPrg*";
            private static readonly int iVesselCountPerTick = Settings.GetValue<int>(Settings.Key.TickCount_VesselsToPurge);
            private static readonly int iItemStackMoveCountPerTick = Settings.GetValue<int>(Settings.Key.TickCount_VesselsToPurge);
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                // Script Content Data
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }
                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {
                    // add Headline
                    displayManager.SetFormattedHeadline(Locales.GetValue(lng, Locales.Key.Headline_Main_BoxPurge));
                    // laod setting
                    String sItemStructureFile = Settings.GetValue<String>(Settings.Key.FileName_ItemStructureTree);
                    if (!ItemGroups.Init(root, sItemStructureFile))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoItemStructureFile), sItemStructureFile));
                    }
                    deviceProcessors.ForEach(processor =>
                    {
                        displayManager.AddInfoDisplays(GenericMethods.SplitArguments(processor.CustomName));
                    });
                    // Service manager for vessels and data
                    CargoTransferManager cargoManager = new CargoTransferManager(CsRoot, E, lng);
                    List<String> vesselInfoTableLineList = new List<String>();
                    Dictionary<IEntityData, String> rejectedVesselList = new Dictionary<IEntityData, String>(); // vessel and rejectreason
                    Dictionary<IEntityData, double> remainingVesselList = new Dictionary<IEntityData, double>(); // vessel and completenesslevel
                    // test manager readyness
                    if (cargoManager.IsVesselReady(E, true, CargoManagementTags.ContainerTag_Input, CargoManagementTags.CargoInputActiveTag, out String sError))
                    {
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedSettingsReadIn)));
                        CsRoot.EntitiesByName("*").ForEach(vessel =>
                        {
                            // test receiver vessel validity
                            if (cargoManager.IsVesselValid(vessel))
                            {
                                // test receiver vessel readyness
                                if (!cargoManager.IsVesselReady(vessel, true, CargoManagementTags.ContainerTag_Output, CargoManagementTags.CargoOutputActiveTag, out sError))
                                {
                                    rejectedVesselList.Add(vessel, sError);
                                }
                            }
                        });
                        // proecess vessels by priority
                        int iVesselProcessedCount = 0;
                        int iItemMovedCount = 0;
                        bool bSomeItemMoved;
                        String sRemoteContainerName;
                        cargoManager.GetPriorityOrderedValidRequestors().ForEach(vesselSetting =>
                        {
                            // iterate all items to move
                            bSomeItemMoved = false;
                            sRemoteContainerName = cargoManager.GetRemoteContainerName(vesselSetting.E);
                            CsRoot.Items(vesselSetting.E.S, sRemoteContainerName)?.ForEach(stack =>
                            {
                                // artificial conveyor simulation
                                if (iVesselProcessedCount < iVesselCountPerTick && iItemMovedCount < iItemStackMoveCountPerTick)
                                {
                                    // Move
                                    CsRoot.Move(stack, E.S, cargoManager.GetManagerContainerName());
                                    // moved item count
                                    bSomeItemMoved = true;
                                    iItemMovedCount++;
                                }
                            });
                            // vessel count
                            if (bSomeItemMoved)
                            {
                                iVesselProcessedCount++;
                            }
                            double progress = GenericMethods.ComputeContainerUsage(CsRoot, vesselSetting.E, sRemoteContainerName, true);
                            if (progress > 0)
                            {
                                remainingVesselList.Add(vesselSetting.E, (1 - progress));
                            }
                        });
                        displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedCargoTransfer)));
                        // compute info results
                        // remaining vessels
                        if (remainingVesselList.Count() > 0)
                        {
                            vesselInfoTableLineList.Clear();
                            remainingVesselList.ForEach(vessel =>
                            {
                                vesselInfoTableLineList.Add(String.Format(" -{0} {1} {2:P2}", vessel.Key.Name, Locales.GetValue(lng, Locales.Key.Text_BoxPurge_VesselProgressText), vessel.Value));
                            });
                            displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_BoxPurge_Table_VesselsRemaining), vesselInfoTableLineList.ToArray());
                        }
                        // vessels with error
                        if (rejectedVesselList.Count() > 0)
                        {
                            vesselInfoTableLineList.Clear();
                            rejectedVesselList.ForEach(vessel =>
                            {
                                vesselInfoTableLineList.Add(String.Format(" -{0}: {1}", vessel.Key.Name, vessel.Value));
                            });
                            displayManager.AddSimpleInfoTable(Locales.GetValue(lng, Locales.Key.Headline_BoxPurge_Table_VesselsRejected), vesselInfoTableLineList.ToArray());
                        }
                        // standby
                        if (remainingVesselList.Count() == 0 && rejectedVesselList.Count() == 0)
                        {
                            displayManager.AddPlainText(String.Format("{0}{1}{2}", Environment.NewLine,
                                Settings.GetValue<String>(Settings.Key.DisplayColor_Fine), Locales.GetValue(lng, Locales.Key.Text_StandBy)));
                        }
                    }
                    else
                    {
                        displayManager.AddLogEntry(String.Format("- {0}", sError));
                        displayManager.AddPlainText(sError);
                    }
                    // draw info panel
                    if (displayManager.DrawFormattedInfoView()) { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedInfoPanelDraw))); }
                    else { displayManager.AddLogEntry(String.Format("- {0}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_NoInfoPanelFound))); }
                    // error printing
                    List<KeyValuePair<String, String>> errorsTable = cargoManager.GetManagerSettings().GetFaultyParameters();
                    if (errorsTable.Count() > 0)
                    {
                        displayManager.AddLogEntry(Locales.GetValue(lng, Locales.Key.Headline_ErrorTable_FaultyParameter), errorsTable);
                    }
                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
    }
    namespace PersistentDataStorage
    {
        using CpuInfCpu = Scripts.CpuInfCpu;
        using CpuInfDev = Scripts.CpuInfDev;
        using CpuInfHll = Scripts.CpuInfHll;
        using DisplayViewManager = DisplayViewManager.DisplayViewManager;
        using Locales = Locales.Locales;
        using GenericMethods = GenericMethods.GenericMethods;
        using Settings = Settings.Settings;
        public static class PersistentDataStorage
        {
            private static readonly ConcurrentDictionary<int, VesselDataStorage> registeredVesselsList = new ConcurrentDictionary<int, VesselDataStorage>();
            private class VesselDataStorage
            {
                public ConcurrentDictionary<String, DisplayViewManager.RegisteredDeviceStatusData> RegisteredDeviceStatusData = 
                    new ConcurrentDictionary<String, DisplayViewManager.RegisteredDeviceStatusData>();
                public ConcurrentDictionary<VectorInt3, CpuInfDev.RegisteredDeviceDataSet> RegisteredDeviceDataByPosition =
                    new ConcurrentDictionary<VectorInt3, CpuInfDev.RegisteredDeviceDataSet>();
                public ConcurrentDictionary<VectorInt3, CpuInfCpu.RegisteredCpuDataSet> RegisteredCpuDataByPosition =
                    new ConcurrentDictionary<VectorInt3, CpuInfCpu.RegisteredCpuDataSet>();
                public CpuInfHll.RegisteredStructureDataSet RegisteredStructureData = 
                    new CpuInfHll.RegisteredStructureDataSet();
            }
            public static void CheckDataReset(IScriptModData root, Locales.Language lng)
            {
                String sProcessorName = "ResetData";
                ICsScriptFunctions CsRoot = root.CsRoot;
                IEntityData E = root.E;
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sProcessorName);
                if (E.S.IsPowerd && deviceProcessors != null && deviceProcessors.Count() > 0)
                {
                    DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                    displayManager.AddLogDisplays(sProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                    try
                    {
                        registeredVesselsList.TryRemove(E.Id, out _);
                        displayManager.AddLogEntry(String.Format("- {0}: {1}", Locales.GetValue(lng, Locales.Key.Text_CpuLog_FinishedDataReset), E.Name));
                    }
                    catch (Exception e)
                    {
                        displayManager.AddLogEntry(e);
                    }
                }
            }
            public static DisplayViewManager.RegisteredDeviceStatusData GetRegisteredDeviceStatusData(IEntityData E, String sGroupTag)
            {
                lock (registeredVesselsList) {
                    VesselDataStorage vesselData = GetVesselDataStorage(E);
                    if (!vesselData.RegisteredDeviceStatusData.TryGetValue(sGroupTag, out DisplayViewManager.RegisteredDeviceStatusData deviceStatusData))
                    {
                        deviceStatusData = new DisplayViewManager.RegisteredDeviceStatusData(sGroupTag);
                        vesselData.RegisteredDeviceStatusData.TryAdd(sGroupTag, deviceStatusData);
                    }
                    return deviceStatusData;
                }
            }
            public static ConcurrentDictionary<VectorInt3, CpuInfDev.RegisteredDeviceDataSet> GetRegisteredDevicesList(IEntityData E)
            {
                VesselDataStorage vesselData = GetVesselDataStorage(E);
                return vesselData.RegisteredDeviceDataByPosition;
            }
            public static ConcurrentDictionary<VectorInt3, CpuInfCpu.RegisteredCpuDataSet> GetRegisteredCpusList(IEntityData E)
            {
                VesselDataStorage vesselData = GetVesselDataStorage(E);
                return vesselData.RegisteredCpuDataByPosition;
            }
            public static CpuInfHll.RegisteredStructureDataSet GetRegisteredStructureDataSet(IEntityData E)
            {
                lock (registeredVesselsList) {
                    VesselDataStorage vesselData = GetVesselDataStorage(E);
                    return vesselData.RegisteredStructureData;
                }
            }
            private static VesselDataStorage GetVesselDataStorage(IEntityData E)
            {
                if (!registeredVesselsList.TryGetValue(E.Id, out VesselDataStorage vesselData))
                {
                    vesselData = new VesselDataStorage();
                    registeredVesselsList.TryAdd(E.Id, vesselData);
                }
                return vesselData;
            }
        }
    }
    namespace CargoTransferManager
    {
        using Locales = Locales.Locales;
        using SettingsDataManager = SettingsDataManager.SettingsDataManager;
        using CargoManagementTags = SettingsDataManager.CargoManagementTags;
        public class CargoTransferManager
        {
            private readonly ICsScriptFunctions CsRoot;
            private readonly IEntityData E;
            private readonly Locales.Language Language;
            private String sManagerContainerName = null;
            private SettingsDataManager managerSettings = null;
            private readonly List<SettingsDataManager> remoteSettingsList = new List<SettingsDataManager>();
            private readonly Dictionary<IEntityData, String> remoteContainerNameList = new Dictionary<IEntityData, String>();
            private readonly Dictionary<IEntityData, int> remoteValidityAndPriorityList = new Dictionary<IEntityData, int>();
            const int iRemoteConnectionBlockId = 1627;
            public CargoTransferManager(ICsScriptFunctions rootFunctions, IEntityData vessel, Locales.Language lng)
            {
                CsRoot = rootFunctions;
                E = vessel;
                Language = lng;
            }
            public bool IsVesselReady(IEntityData vessel, bool bPurgeMode, String sContainerTag, String sCargoTransferSwitchTag, out String sError)
            {
                bool bResult = false;
                sError = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_Unknown);

                bool bManagerMode = vessel.Equals(E);
                SettingsDataManager settings = new SettingsDataManager(CsRoot, vessel, Language);
                bool bReadInState = settings.ReadInSettingsTable(Locales.GetValue(Language, Locales.Key.SettingsTableDeviceName));
                String sContainerName = settings.GetParameterValue<String>(sContainerTag);
                String sOnState = settings.GetParameterValue<String>(sCargoTransferSwitchTag);
                int? iPriority = null;
                if (bManagerMode)
                {
                    managerSettings = settings;
                    sManagerContainerName = sContainerName;
                    remoteSettingsList.Clear();
                    remoteContainerNameList.Clear();
                    remoteValidityAndPriorityList.Clear();
                }
                else
                {
                    iPriority = settings.GetParameterValue<int?>(CargoManagementTags.VesselPriorityTag);
                    remoteContainerNameList.Add(vessel, sContainerName);
                    remoteSettingsList.Add(settings);
                }
                IBlockData remoteDevice = CsRoot.Devices(vessel.S, "*").Where(c => c.Id == iRemoteConnectionBlockId).FirstOrDefault();
                IBlockData container = CsRoot.Devices(vessel.S, sContainerName ?? "").FirstOrDefault();
                if (bReadInState
                    && remoteDevice != null
                    && remoteDevice.Active
                    && container != null
                    && sOnState == CargoManagementTags.CargoActiveTag_On)
                {
                    if (!bManagerMode)
                    {
                        if (iPriority.HasValue)
                        {
                            remoteValidityAndPriorityList.Add(vessel, iPriority.Value);
                            bResult = true;
                        }
                        else
                        {
                            if (bPurgeMode) { sError = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_BoxPurge_PriorityMissing); }
                            else { sError = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_BoxFill_PriorityMissing); }
                        }
                    }
                    else
                    {
                        bResult = true;
                    }
                }
                else
                {
                    if (!bReadInState)
                    {
                        sError = String.Format("{0}: {1}",
                            Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_SettingsTableMissing), 
                            Locales.GetValue(Language, Locales.Key.SettingsTableDeviceName));
                    }
                    else if (remoteDevice == null)
                    {
                        sError = String.Format("{0} {1}", CsRoot.I18n(iRemoteConnectionBlockId), Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_NotFound));
                    }
                    else if (!remoteDevice.Active)
                    {
                        sError = String.Format("{0} {1}", CsRoot.I18n(iRemoteConnectionBlockId), Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_IsOffline));
                    }
                    else if (container == null)
                    {
                        if (bPurgeMode ^ bManagerMode) { sError = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_OutputContainerNotFound); }
                        else { sError = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_InputContainerNotFound); }
                        sError = String.Format("{0}: {1}", sError, sContainerName);
                    }
                    else if (sOnState != CargoManagementTags.CargoActiveTag_On)
                    {
                        if (bPurgeMode && bManagerMode) { sError = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_BoxPurge_ManagerTransferSwitchOff); }
                        else if (bPurgeMode && !bManagerMode) { sError = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_BoxPurge_RemoteTransferSwitchOff); }
                        else if (!bPurgeMode && bManagerMode) { sError = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_BoxFill_ManagerTransferSwitchOff); }
                        else { sError = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_BoxFill_RemoteTransferSwitchOff); }
                    }
                }
                return bResult;
            }
            public bool IsVesselValid(IEntityData vessel)
            {
                bool bResult = false;
                if (vessel.Id != E.Id
                    && vessel.S.IsPowerd
                    && vessel.Faction.Id == E.Faction.Id
                    && vessel.EntityType != EntityType.BA)
                {
                    bResult = true;
                }
                return bResult;
            }
            public List<SettingsDataManager> GetPriorityOrderedValidRequestors()
            {
                return remoteSettingsList.Where(vesselSetting => remoteValidityAndPriorityList.ContainsKey(vesselSetting.E))
                    .OrderByDescending(vesselSetting => remoteValidityAndPriorityList[vesselSetting.E]).ToList();
            }
            public String GetManagerContainerName()
            {
                return sManagerContainerName;
            }
            public String GetRemoteContainerName(IEntityData vessel)
            {
                if (remoteContainerNameList.TryGetValue(vessel, out String sContainer))
                {
                    return sContainer;
                }
                else
                {
                    return null;
                }
            }
            public SettingsDataManager GetManagerSettings()
            {
                return managerSettings;
            }
        }
    }
    namespace SettingsDataManager
    {
        using Locales = Locales.Locales;
        using ItemGroups = ItemGroups.ItemGroups;
        public class SettingsDataManager
        {
            private readonly ICsScriptFunctions CsRoot;
            public IEntityData E { get; }
            private readonly Locales.Language Language;

            private const Char cCargoManagementListLineSeperator = '\n';
            private const Char cCargoManagementListItemSeperator = ':';

            public readonly List<KeyValuePair<String, String>> CompleteSettingsTable = new List<KeyValuePair<String, String>>();
            private readonly List<KeyValuePair<String, String>> unknownItemsTable = new List<KeyValuePair<String, String>>();
            private readonly List<KeyValuePair<String, String>> unknownBoxesTable = new List<KeyValuePair<String, String>>();
            private readonly List<KeyValuePair<String, String>> faultyParametersTable = new List<KeyValuePair<String, String>>();

            public SettingsDataManager(ICsScriptFunctions rootFunctions, IEntityData vessel, Locales.Language lng)
            {
                CsRoot = rootFunctions;
                E = vessel;
                Language = lng;
            }
            public bool ReadInSettingsTable(String sSettingsTableDeviceName)
            {
                CompleteSettingsTable.Clear();
                unknownItemsTable.Clear();
                unknownBoxesTable.Clear();
                faultyParametersTable.Clear();

                bool bResult = false;
                ILcd iLCDDataTable = CsRoot.GetDevices<ILcd>(CsRoot.Devices(E.S, sSettingsTableDeviceName)).FirstOrDefault();
                if (iLCDDataTable != null)
                {
                    iLCDDataTable.GetText().Split(cCargoManagementListLineSeperator).ForEach(sLine =>
                    {
                        string[] sLineItems = sLine.Split(cCargoManagementListItemSeperator);
                        int iElementCount = sLineItems.Count();
                        if (iElementCount == 2)
                        {
                            CompleteSettingsTable.Add(new KeyValuePair<String, String>(sLineItems[0].Trim(), sLineItems[1].Trim()));
                        }
                        else if (iElementCount == 1)
                        {
                            CompleteSettingsTable.Add(new KeyValuePair<String, String>(sLineItems[0].Trim(), ""));
                        }
                    });
                    if (CompleteSettingsTable.Count > 0)
                    {
                        bResult = true;
                    }
                }
                return bResult;
            }
            public T GetParameterValue<T>(String sParameterName, bool bNoErrorTracking = false)
            {
                String sValue = CompleteSettingsTable.FirstOrDefault(entry => entry.Key.Equals(sParameterName)).Value;
                ConvertParameter<T>(sValue, sParameterName, bNoErrorTracking, out T value);
                return value;
            }
            public List<KeyValuePair<String, T>> GetSettingsTable<T>(KeyValuePair<string, string> container)
            {
                return GetSettingsTable<T>(container.Key, container.Value);
            }
            public List<KeyValuePair<String, T>> GetSettingsTable<T>(string sItemListHeadlineTag)
            {
                return GetSettingsTable<T>(sItemListHeadlineTag, null);
            }
            private List<KeyValuePair<String, T>> GetSettingsTable<T>(string key, string value)
            {
                List<KeyValuePair<String, T>> table = new List<KeyValuePair<String, T>>();
                bool bInExpectedArea = false;
                bool bHeadlineFound = false;

                CompleteSettingsTable.ForEach(setting =>
                {
                    if (!bInExpectedArea)
                    {
                        if (bHeadlineFound == true)
                        {
                            return;
                        }
                        if (setting.Key.Equals(key) && (value == null || setting.Value.Equals(value)))
                        {
                            bInExpectedArea = true;
                            bHeadlineFound = true;
                        }
                    }
                    else
                    {
                        if (setting.Key.Equals(String.Empty))
                        {
                            bInExpectedArea = false;
                        }
                        else if (!setting.Value.Equals(String.Empty))
                        {
                            if (ConvertParameter<T>(setting.Value, setting.Key, false, out T typedValue))
                            {
                                table.Add(new KeyValuePair<String, T>(setting.Key, typedValue));
                            }
                        }
                    }
                });
                if (bHeadlineFound == false)
                {
                    string parameterName = value == null ? key : String.Join(", ", key, value);
                    faultyParametersTable.Add(new KeyValuePair<String, String>(parameterName,
                        Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_ParameterNotFound)));
                }
                return table;
            }
            private bool ConvertParameter<T>(String sParameter, String sParameterName, bool bNoErrorTracking, out T value)
            {
                value = default;
                bool bResult = false;
                if (!String.IsNullOrEmpty(sParameter))
                {
                    TypeConverter converter = TypeDescriptor.GetConverter(typeof(T));
                    if (converter != null)
                    {
                        try
                        {
                            value = (T)converter.ConvertFromString(sParameter);
                            bResult = true;
                        }
                        catch (Exception)
                        {
                            if (!bNoErrorTracking)
                            {
                                faultyParametersTable.Add(new KeyValuePair<String, String>(sParameterName,
                                    Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_ParameterNotConvertable)));
                            }
                        }
                    }
                    else if (!bNoErrorTracking)
                    {
                        faultyParametersTable.Add(new KeyValuePair<String, String>(sParameterName,
                            String.Format("{0}: {1}", Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_ConverterNotFound), typeof(T).FullName)));
                    }
                }
                else if (!bNoErrorTracking)
                {
                    faultyParametersTable.Add(new KeyValuePair<String, String>(sParameterName,
                        Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_ParameterNotFound)));
                }
                return bResult;
            }
            public String GetContainerNameByItemId(int iItemId)
            {
                // das geht besser/stabiler/wartungsfreundlicher/plausibler, sobald die api jemals eine abrufbare ingame gruppen vererbungsstruktur für items hergibt
                String sTargetContainerTag = null;
                ItemGroups.GetAllGroupsById(iItemId)?.ForEach(sGroup =>
                {
                    if (sTargetContainerTag != null) return;
                    switch (sGroup)
                    {
                        case ItemGroups.Ores: sTargetContainerTag = CargoManagementTags.ContainerTag_Ore; break;
                        case ItemGroups.Ingots: sTargetContainerTag = CargoManagementTags.ContainerTag_Ingot; break;
                        case ItemGroups.Components: sTargetContainerTag = CargoManagementTags.ContainerTag_Component; break;
                        case ItemGroups.StructureBlocksMulti:
                        case ItemGroups.StructureBlocksL: sTargetContainerTag = CargoManagementTags.ContainerTag_BlockL; break;
                        case ItemGroups.WingsS:
                        case ItemGroups.StructureBlocksS: sTargetContainerTag = CargoManagementTags.ContainerTag_BlockS; break;
                        case ItemGroups.Medicines: sTargetContainerTag = CargoManagementTags.ContainerTag_Medic; break;
                        case ItemGroups.Foods: sTargetContainerTag = CargoManagementTags.ContainerTag_Food; break;
                        case ItemGroups.Ingredients: sTargetContainerTag = CargoManagementTags.ContainerTag_Ingredient; break;
                        case ItemGroups.Sprouts: sTargetContainerTag = CargoManagementTags.ContainerTag_Sprout; break;
                        case ItemGroups.Armor:
                        case ItemGroups.ToolsPlaceable:
                        case ItemGroups.ToolsPlayer: sTargetContainerTag = CargoManagementTags.ContainerTag_Tool; break;
                        case ItemGroups.ArmorMods: sTargetContainerTag = CargoManagementTags.ContainerTag_ArmorMod; break;
                        case ItemGroups.DevicesMulti:
                        case ItemGroups.DevicesL: sTargetContainerTag = CargoManagementTags.ContainerTag_DeviceL; break;
                        case ItemGroups.DevicesS: sTargetContainerTag = CargoManagementTags.ContainerTag_DeviceS; break;
                        case ItemGroups.WeaponsPlayer: sTargetContainerTag = CargoManagementTags.ContainerTag_WeaponPlayer; break;
                        case ItemGroups.AmmoAll: sTargetContainerTag = CargoManagementTags.ContainerTag_Ammo; break;
                        case ItemGroups.Refills: sTargetContainerTag = CargoManagementTags.ContainerTag_Refills; break;
                        case ItemGroups.Treasures: sTargetContainerTag = CargoManagementTags.ContainerTag_Treasure; break;
                        default: break;
                    }
                });
                if (!String.IsNullOrEmpty(sTargetContainerTag))
                {
                    if (TryGetContainerName(sTargetContainerTag, out String sTargetContainerName))
                    {
                        return sTargetContainerName;
                    }
                }
                else
                {
                    unknownItemsTable.Add(new KeyValuePair<String, String>(iItemId.ToString(), CsRoot.I18n(iItemId)));
                }
                return null;
            }
            public String GetContainerNameByTag(String sContainerTagName, bool bNoErrorTracking = false)
            {
                if (!String.IsNullOrEmpty(sContainerTagName))
                {
                    if (TryGetContainerName(sContainerTagName, out String sTargetContainerName, bNoErrorTracking))
                    {
                        return sTargetContainerName;
                    }
                }
                return null;
            }
            private bool TryGetContainerName(String sContainerTagName, out String sContainerName, bool bNoErrorTracking = false)
            {
                sContainerName = GetParameterValue<String>(sContainerTagName, bNoErrorTracking);
                if (!String.IsNullOrEmpty(sContainerName))
                {
                    if (CsRoot.Devices(E.S, sContainerName).Count() > 0)
                    {
                        return true;
                    }
                    else
                    {
                        unknownBoxesTable.Add(new KeyValuePair<String, String>(sContainerName, Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_DeviceNotFound)));
                    }
                }
                return false;
            }
            public List<KeyValuePair<String, String>> GetFaultyParameters()
            {
                return faultyParametersTable;
            }
            public List<KeyValuePair<String, String>> GetUnknownBoxes()
            {
                return unknownBoxesTable;
            }
            public List<KeyValuePair<String, String>> GetUnknownItems()
            {
                return unknownItemsTable;
            }
            public List<KeyValuePair<String, String>> GetSafetyStockContainers(bool bWithoutAmmo = false)
            {
                List<KeyValuePair<String, String>> containerList = new List<KeyValuePair<String, String>>();
                CompleteSettingsTable.Where(entry => bWithoutAmmo
                    ? entry.Key.Equals(CargoManagementTags.SafetyStockTag)
                    : entry.Key.Contains(CargoManagementTags.SafetyStockTag)
                    )?.ForEach(entry =>
                {
                    containerList.Add(entry);
                });
                return containerList;
            }
        }
        public static class CargoManagementTags
        {
            public const String CargoActiveTag_On = "on";

            public const String CargoOutputActiveTag = "Output";
            public const String CargoInputActiveTag = "Input";
            public const String VesselPriorityTag = "Priority";

            public const String FluidLevelsHeadlineTag = "FluidLevels";
            public const String ContainerTagsHeadlineTag = "Container";

            public const String SafetyStockAmmoHeadlineTag = "SafetyStockAmmo";
            public const String SafetyStockTag = "SafetyStock";

            public const String FluidLevelTag_Fuel = "FuelLevel";
            public const String FluidLevelTag_Oxygen = "OxygenLevel";
            public const String FluidLevelTag_Pentaxid = "PentaxidLevel";

            public const String ContainerTag_Output = "OutputBox";
            public const String ContainerTag_Input = "InputBox";
            public const String ContainerTag_SafetySource = "StockSourceBox";
            public const String ContainerTag_Ore = "OreBox";
            public const String ContainerTag_Ingot = "IngotBox";
            public const String ContainerTag_Component = "ComponentBox";
            public const String ContainerTag_BlockL = "BlockLargeBox";
            public const String ContainerTag_BlockS = "BlockSmallBox";
            public const String ContainerTag_Medic = "MedicBox";
            public const String ContainerTag_Food = "FoodBox";
            public const String ContainerTag_Ingredient = "IngredientBox";
            public const String ContainerTag_Sprout = "SproutBox";
            public const String ContainerTag_Tool = "EquipBox";
            public const String ContainerTag_ArmorMod = "ModBox";
            public const String ContainerTag_DeviceL = "DeviceLargeBox";
            public const String ContainerTag_DeviceS = "DeviceSmallBox";
            public const String ContainerTag_WeaponPlayer = "WeaponBox";
            public const String ContainerTag_Ammo = "AmmoBox";
            public const String ContainerTag_Refills = "RefillsBox";
            public const String ContainerTag_Treasure = "TreasureBox";
        }
    }
    namespace ItemGroups
    {
        public static class ItemGroups
        {
            //predefined groups
            public const String ItemsAll = "ItemsAll";
            public const String Useables = "Useables";
            public const String AmmoAll = "AmmoAll";
            public const String AmmoMulti = "AmmoMulti";
            public const String AmmoPlayer = "AmmoPlayer";
            public const String AmmoVessel = "AmmoVessel";
            public const String AmmoVesselMulti = "AmmoVesselMulti";
            public const String AmmoVesselHV = "AmmoVesselHV";
            public const String AmmoVesselSV = "AmmoVesselSV";
            public const String AmmoVesselCV = "AmmoVesselCV";
            public const String AmmoVesselBA = "AmmoVesselBA";
            public const String Medicines = "Medicines";
            public const String Refills = "Refills";
            public const String Fuel = "Fuel";
            public const String Oxygen = "Oxygen";
            public const String Pentaxid = "Pentaxid";
            public const String WeaponsPlayer = "WeaponsPlayer";
            public const String ToolsPlayer = "ToolsPlayer";
            public const String ToolsPlaceable = "ToolsPlaceable";
            public const String Armor = "Armor";
            public const String ArmorMods = "ArmorMod";
            public const String Sprouts = "Sprouts";
            public const String Foods = "Foods";
            public const String Treasures = "Treasures";
            public const String ComponentsAll = "ComponentsAll";
            public const String Ores = "Ores";
            public const String Ingots = "Ingots";
            public const String Components = "Components";
            public const String Ingredients = "Ingredients";

            public const String Blocks = "Blocks";
            public const String StructureBlocksMulti = "StructureBlocksMulti";
            public const String DevicesMulti = "DevicesMulti";
            public const String DecosMulti = "DecosMulti";
            public const String AntennasMulti = "AntennasMulti";
            public const String LightsMulti = "LightsMulti";
            public const String ContainersMulti = "ContainersMulti";
            public const String CpusMulti = "CpusMulti";
            public const String MiscDevicesMulti = "MiscDevicesMulti";

            public const String BlocksS = "BlocksS";
            public const String StructureBlocksS = "StructureBlocksS";
            public const String WingsS = "WingsS";
            public const String DevicesS = "DevicesS";
            public const String CpusS = "CpusS";
            public const String AntennasS = "AntennasS";
            public const String SensorsS = "SensorsS";
            public const String LightsS = "LightsS";
            public const String LandingGearsS = "LandingGearsS";
            public const String CockpitsS = "CockpitsS";
            public const String DoorsS = "DoorsS";
            public const String FridgesS = "FridgesS";
            public const String TanksS = "TanksS";
            public const String ShieldsS = "ShieldsS";
            public const String ThrustersS = "ThrustersS";
            public const String RcssS = "RcssS";
            public const String EnergySourcesS = "EnergySourcesS";
            public const String AssemblersS = "AssemblersS";
            public const String MedBaysS = "MedBaysS";
            public const String MiscDevicesS = "MiscDevicesS";
            public const String HoversS = "HoversS";
            public const String ToolsS = "ToolsS";
            public const String ToolsSawS = "ToolsSawS";
            public const String ToolsDrillS = "ToolsDrillS";
            public const String WeaponsS = "WeaponsS";
            public const String WeaponsRailgunS = "WeaponsRailgunS";
            public const String WeaponsMinigunS = "WeaponsMinigunS";
            public const String WeaponsLaserS = "WeaponsLaserS";
            public const String WeaponsPlasmaS = "WeaponsPlasmaS";
            public const String WeaponsRocketS = "WeaponsRocketS";
            public const String TurretsS = "TurretsS";
            public const String TurretsToolS = "TurretsToolS";
            public const String TurretsWeaponS = "TurretsWeaponS";
            public const String TurretsMinigunS = "TurretsMinigunS";
            public const String TurretsRocketS = "TurretsRocketS";
            public const String TurretsArtyS = "TurretsArtyS";
            public const String TurretsPlasmaS = "TurretsPlasmaS";
            public const String ContainersS = "ContainersS";
            public const String ContainerAmmoS = "ContainerAmmoS";
            public const String ContainerHarvestS = "ContainerHarvestS";
            public const String ContainerCargoS = "ContainerCargoS";

            public const String BlocksL = "BlocksL";
            public const String StructureBlocksL = "StructureBlocksL";
            public const String DevicesL = "DevicesL";
            public const String DecosL = "DecosL";
            public const String CpusL = "CpusL";
            public const String AntennasL = "AntennasL";
            public const String SensorsL = "SensorsL";
            public const String LightsL = "LightsL";
            public const String LandingGearsL = "LandingGearsL";
            public const String CockpitsL = "CockpitsL";
            public const String DoorsL = "DoorsL";
            public const String FridgesL = "FridgesL";
            public const String TanksL = "TanksL";
            public const String ShieldsL = "ShieldsL";
            public const String ThrustersL = "ThrustersL";
            public const String RcssL = "RcssL";
            public const String EnergySourcesL = "EnergySourcesL";
            public const String AssemblersL = "AssemblersL";
            public const String MedBaysL = "MedBaysL";
            public const String MiscDevicesL = "MiscDevicesL";
            public const String ToolsL = "ToolsL";
            public const String ToolsDrillL = "ToolsDrillL";
            public const String WeaponsL = "WeaponsL";
            public const String WeaponsLaserL = "WeaponsLaserL";
            public const String WeaponsRocketL = "WeaponsRocketL";
            public const String TurretsL = "TurretsL";
            public const String TurretsToolL = "TurretsToolL";
            public const String TurretsWeaponL = "TurretsWeaponL";
            public const String TurretsMinigunL = "TurretsMinigunL";
            public const String TurretsCannonL = "TurretsCannonL";
            public const String TurretsFlakL = "TurretsFlakL";
            public const String TurretsRocketL = "TurretsRocketL";
            public const String TurretsArtyL = "TurretsArtyL";
            public const String TurretsPlasmaL = "TurretsPlasmaL";
            public const String TurretsSentryL = "TurretsSentryL";
            public const String TurretsLaserL = "TurretsLaserL";
            public const String ContainersL = "ContainersL";
            public const String ContainerAmmoL = "ContainerAmmoL";
            public const String ContainerHarvestL = "ContainerHarvestL";
            public const String ContainerCargoL = "ContainerCargoL";

            // class internals
            private const String sAttributeTagNode = "Node";
            private const String sNodeOpener = "{";
            private const String sNodeCloser = "}";
            private const Char cAttributeTagSeperator = ':';
            private const Char cAttributeItemSeperator = ',';
            private const int iLengthTagNode = 5;

            private static bool IsPathInitialized = false;
            private static DateTime storedFileChangeDate;
            private static String sItemStructFilePath = "";
            private static HelpersTools.FileContent itemStructFileContent;
            private static TreeNode<String> ItemGroupTable;
            public static bool Init(IScriptModData rootObject, String sFileName)
            {
                if (IsPathInitialized == false)
                {
                    if (rootObject is IScriptSaveGameRootData root)
                    {
                        try
                        {
                            sItemStructFilePath = Path.Combine(root.MainScriptPath, "..", sFileName);
                            ParseItemStructFile();
                            IsPathInitialized = true;
                        }
                        catch (Exception e) {
                            throw new Exception(e.ToString()); 
                        }
                    }
                }
                return IsPathInitialized;
            }
            private static void ParseItemStructFile()
            {
                if (File.Exists(sItemStructFilePath))
                {
                    DateTime lastFileChangeDate = File.GetLastWriteTime(sItemStructFilePath);
                    if (!lastFileChangeDate.Equals(storedFileChangeDate))
                    {
                        itemStructFileContent = HelpersTools.GetFileContent(sItemStructFilePath);
                        if (itemStructFileContent != null)
                        {
                            storedFileChangeDate = lastFileChangeDate;
                            String[] sFileLines = itemStructFileContent.Lines;
                            int iStart;
                            TreeNode<String> node = null;
                            sFileLines.ForEach(sLine =>
                            {
                                if (sLine.Contains(sNodeOpener))
                                {
                                    iStart = sLine.IndexOf(sAttributeTagNode + cAttributeTagSeperator);
                                    if (iStart > 0)
                                    {
                                        sLine = sLine.Substring(iStart + iLengthTagNode).Trim();
                                    }
                                    if (node == null)
                                    {
                                        ItemGroupTable = new TreeNode<String>(sLine);
                                        node = ItemGroupTable;
                                    }
                                    else
                                    {
                                        node = node.AddChild(sLine);
                                    }
                                }
                                else if (sLine.Contains(sNodeCloser))
                                {
                                    node = node?.Parent;
                                }
                                else
                                {
                                    String[] sArray = sLine.Split(cAttributeTagSeperator);
                                    if (sArray.Count() >= 2)
                                    {
                                        node?.AddChild(sArray[0].Trim()).AddChild(sArray[1].Replace(" ", string.Empty));
                                    }
                                }
                            });
                        }
                    }
                }
                else
                {
                    throw new Exception("Access to Class ItemGroup without suitable DataFile detected!");
                }
            }
            public static List<int> GetIdListByGroup(params String[] sGroups)
            {
                List<int> idList = new List<int>();
                TreeNode<String> startNode;
                ParseItemStructFile();
                sGroups.ForEach(sGroup =>
                {
                    startNode = ItemGroupTable?.FirstOrDefault(node => node.Data.Equals(sGroup));
                    startNode?.Where(node => node.IsLeaf).ForEach(node =>
                    {
                        idList.AddRange(node.Data.Split(cAttributeItemSeperator).Select(item => int.TryParse(item, out int id) ? id : 0));
                    });
                });
                return idList.Distinct().ToList();
            }
            public static List<String> GetAllGroupsById(int iItemId)
            {
                ParseItemStructFile();
                List<String> groupList = new List<String>();
                TreeNode<String> tempNode = GetFirstNodeById(iItemId);
                while (tempNode?.Parent != null)
                {
                    groupList.Add(tempNode.Parent.Data);
                    tempNode = tempNode.Parent;
                }
                return groupList;
            }
            public static String GetFirstGroupById(int iItemId)
            {
                ParseItemStructFile();
                TreeNode<String> itemNode = GetFirstNodeById(iItemId);
                return itemNode?.Parent.Data;
            }
            private static TreeNode<String> GetFirstNodeById(int iItemId)
            {
                String sItemId = iItemId.ToString();
                return ItemGroupTable?.Where(node => node.IsLeaf).FirstOrDefault(node => node.Data.Split(cAttributeItemSeperator).Contains(sItemId));
            }
            private class TreeNode<T> : IEnumerable<TreeNode<T>>
            {
                public T Data { get; set; }
                public TreeNode<T> Parent { get; set; }
                public ICollection<TreeNode<T>> Children { get; set; }
                public Boolean IsRoot
                {
                    get { return Parent == null; }
                }
                public Boolean IsLeaf
                {
                    get { return Children.Count == 0; }
                }
                public int Level
                {
                    get
                    {
                        if (this.IsRoot)
                            return 0;
                        return Parent.Level + 1;
                    }
                }
                public TreeNode(T data)
                {
                    this.Data = data;
                    this.Children = new LinkedList<TreeNode<T>>();
                    this.ElementsIndex = new LinkedList<TreeNode<T>>();
                    this.ElementsIndex.Add(this);
                }
                public TreeNode<T> AddChild(T child)
                {
                    TreeNode<T> childNode = new TreeNode<T>(child) { Parent = this };
                    this.Children.Add(childNode);
                    this.RegisterChildForSearch(childNode);
                    return childNode;
                }
                public override string ToString()
                {
                    return Data != null ? Data.ToString() : "[data null]";
                }
                #region searching
                private ICollection<TreeNode<T>> ElementsIndex { get; set; }
                private void RegisterChildForSearch(TreeNode<T> node)
                {
                    ElementsIndex.Add(node);
                    if (Parent != null)
                        Parent.RegisterChildForSearch(node);
                }
                public TreeNode<T> FindTreeNode(Func<TreeNode<T>, bool> predicate)
                {
                    return this.ElementsIndex.FirstOrDefault(predicate);
                }
                #endregion
                #region iterating
                IEnumerator IEnumerable.GetEnumerator()
                {
                    return GetEnumerator();
                }
                public IEnumerator<TreeNode<T>> GetEnumerator()
                {
                    yield return this;
                    foreach (var directChild in this.Children)
                    {
                        foreach (var anyChild in directChild)
                            yield return anyChild;
                    }
                }
                #endregion
            }
        }
    }
    namespace GenericMethods
    {
        using ItemGroups = ItemGroups.ItemGroups;
        using DisplayViewManager = DisplayViewManager.DisplayViewManager;
        using IContainer = Eleon.Modding.IContainer;

        public static class GenericMethods
        {
            private const Char cArgumentSeperator = ':';
            private const string sAttributeTagVolume = "Volume";
            private const string sAttributeTagShieldMaxValue = "ShieldCapacity";

            public static String[] SplitArguments(String sArgs)
            {
                return sArgs.Split(cArgumentSeperator);
            }
            public static double ComputeContainerUsage(ICsScriptFunctions CsRoot, IEntityData vessel, String sContainerName, bool bLimited = false)
            {
                double dMaxCapacity = 0;
                double dUsedCapacity = 0;
                double dTempCapacity;
                object oVolume;

                if (sContainerName != null && !sContainerName.Equals(""))
                {
                    Eleon.Modding.IContainer[] containers = CsRoot.GetDevices<Eleon.Modding.IContainer>(CsRoot.Devices(vessel.S, sContainerName));
                    containers?.ForEach(container =>
                    {
                        dMaxCapacity += container.VolumeCapacity;
                        dTempCapacity = 0;
                        container.GetContent().ForEach(item =>
                        {
                            oVolume = CsRoot.ConfigFindAttribute(item.id, sAttributeTagVolume);
                            if (oVolume != null && double.TryParse(oVolume.ToString(), out double dVolume))
                            {
                                dTempCapacity += item.count * dVolume;
                            }
                        });
                        if (bLimited)
                        {
                            dUsedCapacity += Math.Min(dTempCapacity, container.VolumeCapacity);
                        }
                        else
                        {
                            dUsedCapacity += dTempCapacity;
                        }
                    });
                }
                return (dMaxCapacity == 0 ? 0 : (dUsedCapacity / dMaxCapacity));
            }
            public static double ComputeItemCompletenessLevel(ICsScriptFunctions CsRoot, IEntityData vessel, String sTargetContainer, 
                List<KeyValuePair<String, int>> requestedItemList, out List<KeyValuePair<String, int>> missingItemList)
            {
                Dictionary<String, int> requestedItems = requestedItemList.ToDictionary(item => item.Key, item => item.Value);
                IItemsData[] storedItems = CsRoot.Items(vessel.S, sTargetContainer);
                String sLocalItemName;
                requestedItemList.ForEach(requestedItem =>
                {
                    storedItems.ForEach(storedItem =>
                    {
                        sLocalItemName = CsRoot.I18n(storedItem.Id);
                        if (requestedItem.Key.Equals(sLocalItemName))
                        {
                            if (requestedItems.ContainsKey(requestedItem.Key))
                            {
                                requestedItems[requestedItem.Key] -= storedItem.Count;
                            }
                        }
                    });
                });

                missingItemList = requestedItems.Where(item => item.Value > 0).ToList();

                double dMaxCount = requestedItemList.Sum(item => item.Value);
                double dMissCount = missingItemList.Sum(item => item.Value);
                return (dMaxCount == 0 ? 0 : ((dMaxCount - dMissCount) / dMaxCount));
            }
            public static int IntelligentItemMove(ICsScriptFunctions CsRoot, IEntityData sourceVessel, IEntityData targetVessel, 
                String sSourceBoxName, String sTargetBoxName, KeyValuePair<String, int> item)
            {
                int iMovedItemCount = 0;
                String sItemName = item.Key;
                int iItemValue = item.Value;
                // input safety test
                if (!String.IsNullOrEmpty(sSourceBoxName) && !String.IsNullOrEmpty(sTargetBoxName) && !String.IsNullOrEmpty(sItemName) && iItemValue > 0)
                {
                    // get all target containers by names
                    IContainer[] targetContainer = CsRoot.GetDevices<IContainer>(CsRoot.Devices(targetVessel.S, sTargetBoxName));
                    if (targetContainer != null && targetContainer.Count() > 0)
                    {
                        // get all items from all storage sources by its localized name
                        IItemsData itemStack = CsRoot.Items(sourceVessel.S, sSourceBoxName).FirstOrDefault(stack => sItemName.Equals(CsRoot.I18n(stack.Id)));
                        if (itemStack != null && itemStack.Count > 0)
                        {
                            //Items move and result calc
                            IList<IItemMoveInfo> result = CsRoot.Move(itemStack, targetVessel.S, sTargetBoxName, iItemValue);
                            if (result.Count() > 0) { iMovedItemCount = result.FirstOrDefault().Count; }
                        }
                    }
                }
                return iMovedItemCount;
            }
            public static List<IBlockData> GetBlocksOfGroupTag(ICsScriptFunctions CsRoot, IEntityData vessel, params String[] sGroupTags)
            {
                return CsRoot.Devices(vessel.S, "*").Where(predicate: device => ItemGroups.GetIdListByGroup(sGroupTags)?.Contains(device.Id) == true).ToList();
            }
            public static bool TryGetShieldMaxValue(ICsScriptFunctions CsRoot, IStructureData vessel, out double dShieldMaxValue)
            {
                dShieldMaxValue = -1;

                List<int> shieldDeviceIds = ItemGroups.GetIdListByGroup(ItemGroups.ShieldsS, ItemGroups.ShieldsL);
                List<IBlockData> shieldDevices = CsRoot.Devices(vessel, "*").Where(device => shieldDeviceIds.Any(id => id == device.Id)).ToList();
                if (shieldDevices.Count < 1) { return false; }
                
                object oShieldMaxValue = shieldDevices.Min(device => CsRoot.ConfigFindAttribute(device.Id, sAttributeTagShieldMaxValue));
                return double.TryParse(Convert.ToString(oShieldMaxValue), out dShieldMaxValue);
            }
            public static double GetShieldLevel(ICsScriptFunctions CsRoot, IStructureData vessel)
            {
                if (!vessel.IsShieldActive) { return 0; }
                if (!GenericMethods.TryGetShieldMaxValue(CsRoot, vessel, out double dMaxShieldValue) || dMaxShieldValue == 0) { return 0; }
                return vessel.ShieldLevel / dMaxShieldValue;
            }
            public static double GetPowerLevel(ICsScriptFunctions CsRoot, IStructureData vessel)
            {
                if (!vessel.IsPowerd) { return 0; }
                return vessel.PowerConsumption / (double)vessel.PowerOutCapacity;
            }
        }
    }
    namespace DisplayViewManager
    {
        using PersistentDataStorage = PersistentDataStorage.PersistentDataStorage;
        using GenericMethods = GenericMethods.GenericMethods;
        using Language = Locales.Locales.Language;
        using Locales = Locales.Locales;
        using Settings = Settings.Settings;
        public class DisplayViewManager
        {
            private readonly ICsScriptFunctions CsRoot;
            private readonly IEntityData E;
            private readonly Language Language;

            private static readonly double dLineWidthCorrFactor_Bar = Settings.GetValue<double>(Settings.Key.LineWidthCorrFactor_Bar);
            private static readonly double dLineWidthCorrFactor_TableInfo = Settings.GetValue<double>(Settings.Key.LineWidthCorrFactor_TableInfo);
            private static readonly double dLineWidthCorrFactor_SimpleInfo = Settings.GetValue<double>(Settings.Key.LineWidthCorrFactor_SimpleInfo);
            private static readonly double dLineWidthCorrFactor_State = Settings.GetValue<double>(Settings.Key.LineWidthCorrFactor_State);
            private static readonly double dLineWidthCorrFactor_StructureView = Settings.GetValue<double>(Settings.Key.LineWidthCorrFactor_StructureView);
            private static readonly double dLineWidthCorrFactor_Default = Settings.GetValue<double>(Settings.Key.LineWidthCorrFactor_Default);

            private readonly List<DisplaySet> logDisplaysList = new List<DisplaySet>();
            private readonly List<DisplaySet> infoDisplaysList = new List<DisplaySet>();
            private readonly List<DisplayElement> infoElementList = new List<DisplayElement>();

            private readonly String HtmlColor_Default = Settings.GetValue<String>(Settings.Key.DisplayColor_Default);
            private readonly String HtmlColor_Headline = Settings.GetValue<String>(Settings.Key.DisplayColor_Headline);
            private readonly String HtmlColor_ElementFine = Settings.GetValue<String>(Settings.Key.DisplayColor_Fine);
            private readonly String HtmlColor_ElementWarning = Settings.GetValue<String>(Settings.Key.DisplayColor_Warning);
            private readonly String HtmlColor_ElementCritical = Settings.GetValue<String>(Settings.Key.DisplayColor_Critical);

            private readonly String sDefaultTextFormat = Settings.GetValue<String>(Settings.Key.TextFormat_Default);
            private readonly String sHeadlineTextFormat = Settings.GetValue<String>(Settings.Key.TextFormat_Headline);
            private static readonly int iDeviceStatusSymbolMaxCount = Settings.GetValue<int>(Settings.Key.TextFormat_DeviceStatusMaxSymbolCount);

            private readonly String sStructureView_FullBlockSymbol;
            private readonly String sStructureView_InnerBlockSymbol;
            private readonly String sStructureView_NoBlockSymbol;
            private readonly String sStructureView_SymbolLineSpacing;
            private readonly String sStructureView_FullBlockSpacing;
            private readonly String sStructureView_InnerBlockSpacing;
            private readonly String sStructureView_NoBlockSpacing;

            private static readonly double dDefaultInfoFontSize = Settings.GetValue<double>(Settings.Key.FontSize_DefaultInfo);
            private static readonly double dDefaultLogFontSize = Settings.GetValue<double>(Settings.Key.FontSize_DefaultLog);

            private static readonly double dCompareLevel_Damage_Warning = Settings.GetValue<double>(Settings.Key.CompareLevel_Damage_Warning);
            private static readonly double dCompareLevel_Damage_Critical = Settings.GetValue<double>(Settings.Key.CompareLevel_Damage_Critical);

            public DisplayViewManager(ICsScriptFunctions rootFunctions, IEntityData vessel, Language lng)
            {
                CsRoot = rootFunctions;
                E = vessel;
                Language = lng;
                
                sStructureView_FullBlockSymbol = Locales.GetValue(lng, Locales.Key.Symbol_StatHll_FullBlock);
                sStructureView_InnerBlockSymbol = Locales.GetValue(lng, Locales.Key.Symbol_StatHll_InnerBlock);
                sStructureView_NoBlockSymbol = Locales.GetValue(lng, Locales.Key.Symbol_StatHll_NoBlock);
                sStructureView_SymbolLineSpacing = Locales.GetValue(lng, Locales.Key.Symbol_StatHll_BlockLineSpacing);
                sStructureView_FullBlockSpacing = Locales.GetValue(lng, Locales.Key.Symbol_StatHll_FullBlockSpacing);
                sStructureView_InnerBlockSpacing = Locales.GetValue(lng, Locales.Key.Symbol_StatHll_InnerBlockSpacing);
                sStructureView_NoBlockSpacing = Locales.GetValue(lng, Locales.Key.Symbol_StatHll_NoBlockSpacing);
            }

            public void SetFormattedHeadline(String sHeadline)
            {
                AddHeadline(String.Format("{0}:", sHeadline));
                AddHeadline(String.Format("Update: {0}", DateTime.Now));
                AddHeadline("");
            }
            public void AddStructureView(double?[][][] dStructDamageData, StructureViews eView)
            {
                String sName;
                switch (eView)
                {
                    case StructureViews.eTopView: sName = Locales.GetValue(Language, Locales.Key.Headline_StatHll_TopView); break;
                    case StructureViews.eDeckView: sName = Locales.GetValue(Language, Locales.Key.Headline_StatHll_DeckView); break;
                    default: sName = Locales.GetValue(Language, Locales.Key.Text_ErrorMessage_Unknown); break;
                }
                DisplayElement element = new DisplayElement(ComputeHeadLine(sName), dStructDamageData, eView);
                infoElementList.Add(element);
            }
            public void AddBlankLine(int count = 1)
            {
                for(int i = 0; i<count; i++)
                {
                    infoElementList.Add(new DisplayElement());
                }
            }
            public void AddBar(String sName, double dLevelAct, double dLevelMax, bool bInversColor, int iSegmentCount, double dWarningLevel, double dCriticalLevel)
            {
                double dLevel = (dLevelMax == 0 ? 0 : (dLevelAct / dLevelMax));
                AddBar(sName, dLevel, bInversColor, iSegmentCount, dWarningLevel, dCriticalLevel);
            }
            public void AddBar(String sName, double dLevel, bool bInversColor, int iSegmentCount, double dWarningLevel, double dCriticalLevel)
            {
                DisplayElement element = new DisplayElement(
                    ElementTypes.eBar,
                    ComputeHeadLine(sName),
                    ComputeBar(dLevel, bInversColor, iSegmentCount, dWarningLevel, dCriticalLevel));
                infoElementList.Add(element);
            }
            public void AddDeviceStatus(String sName, String sSymbol, String sDeviceMissingMessage, params String[] sGroupTags)
            {
                AddDeviceStatus(sName, sSymbol, sDeviceMissingMessage, null, false, sGroupTags);
            }
            public void AddDeviceStatus(String sName, String sSymbol, String sDeviceMissingMessage, EntityType? eHideAtType, bool bHideAtZero, params String[] sGroupTags)
            {
                if (eHideAtType == null || eHideAtType.Value != E.EntityType)
                {
                    List<IBlockData> deviceBlocks = GenericMethods.GetBlocksOfGroupTag(CsRoot, E, sGroupTags);
                    RegisteredDeviceStatusData deviceStatus = PersistentDataStorage.GetRegisteredDeviceStatusData(E, String.Join("", sGroupTags));
                    deviceStatus.SetExistsCount(deviceBlocks.Count());
                    if (!bHideAtZero || deviceStatus.MaxCount > 0)
                    {
                        deviceStatus.SetActiveCount(deviceBlocks.Count(device => device.Active));
                        AddDeviceStatus(sName, sSymbol, sDeviceMissingMessage, deviceStatus);
                    }
                }
            }
            private void AddDeviceStatus(String sName, String sSymbol, String sDeviceMissingMessage, RegisteredDeviceStatusData deviceStatus)
            {
                DisplayElement element = new DisplayElement(
                        ElementTypes.eState,
                        ComputeHeadLine(sName),
                        ComputeDeviceStatus(sSymbol, sDeviceMissingMessage, deviceStatus));
                infoElementList.Add(element);
            }
            public void AddSimpleInfoTable(String sName, params String[] sTableItems)
            {

                for (int i = 0; i < sTableItems.Length; i++)
                {
                    sTableItems[i] = ComputeTextLine(sTableItems[i]);
                }
                DisplayElement element = new DisplayElement(
                    ElementTypes.eTableInfo,
                    ComputeHeadLine(sName),
                    sTableItems
                );
                infoElementList.Add(element);
            }
            public void AddPlainText(String sText)
            {
                DisplayElement element = new DisplayElement(
                    ElementTypes.eSimpleInfo,
                    ComputeTextLine(sText),
                    Array.Empty<String>()
                );
                infoElementList.Add(element);
            }
            public void AddHeadline(String sHeadline)
            {
                DisplayElement element = new DisplayElement(
                    ElementTypes.eHeadline,
                    ComputeTextLine(sHeadline),
                    Array.Empty<String>()
                );
                infoElementList.Add(element);
            }
            public void AddInfoDisplays(String[] sArgs)
            {
                Dictionary<int, List<ILcd>> LCDDisplayTable = new Dictionary<int, List<ILcd>>();
                double? dFontSize = null;
                int? iLineCount = null;
                int? iViewOffset = null;
                if (sArgs.Count() >= 2) {
                    IBlockData[] AllLinkedDisplays = CsRoot.Devices(E.S, sArgs[1] + "*");
                    AllLinkedDisplays.ForEach(lcdDevice =>
                    {
                        if (!int.TryParse(lcdDevice.CustomName.Last().ToString(), out int iOrderNumber)) { iOrderNumber = 0; }
                        if (!LCDDisplayTable.TryGetValue(iOrderNumber, out List<ILcd> lcdList)) {
                            lcdList = new List<ILcd>();
                            LCDDisplayTable.Add(iOrderNumber, lcdList);
                        }
                        lcdList.Add(CsRoot.GetDevices<ILcd>(lcdDevice).FirstOrDefault());
                    });
                }
                if (sArgs.Count() >= 3 && double.TryParse(sArgs[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double dTempSize) && dTempSize > 0) { dFontSize = dTempSize; }
                if (sArgs.Count() >= 4 && int.TryParse(sArgs[3], out int iTempCount) && iTempCount > 0) { iLineCount = iTempCount; }
                if (sArgs.Count() >= 5 && int.TryParse(sArgs[4], out int iTempViewOffset)) { iViewOffset = iTempViewOffset; }
                DisplaySet newLcdSet = new DisplaySet(LCDDisplayTable, dDefaultInfoFontSize, dFontSize, iLineCount, iViewOffset);
                infoDisplaysList.Add(newLcdSet);
            }
            public void AddLogDisplays(String sHeadline, String sVersion, ILcd[] lcds)
            {
                Dictionary<int, List<ILcd>> LCDDisplayTable = new Dictionary<int, List<ILcd>> { { 0, lcds.ToList() } };
                DisplaySet newLcdSet = new DisplaySet(LCDDisplayTable, dDefaultLogFontSize, null, null, null);
                logDisplaysList.Add(newLcdSet);
                SetDisplayText(newLcdSet, String.Format("{0} v{3} Log:{1}Update: {2}{1}", sHeadline, Environment.NewLine, DateTime.Now, sVersion));
            }
            public int GetMaxInfoDisplayGroupCount()
            {
                return infoDisplaysList.Select(lcdSet => lcdSet.LCDDisplayTable.Count).Max();
            }
            public void AddLogEntry(Exception e)
            {
                String sEntry = String.Format("{0}Exception:{0}Msg: {1}{0}Inner: {2}{0}Source: {3}{0}Target: {4}{0}Data: {5}{0}Stack: {6}",
                    Environment.NewLine, e.Message, e.InnerException, e.Source, e.TargetSite, e.Data, e.StackTrace);
                AddLogEntry(sEntry);
            }
            public void AddLogEntry(String sEntry)
            {
                AppendDisplayText(logDisplaysList, String.Format("{0}{1}", Environment.NewLine, sEntry));
            }
            public void AddLogEntry(String sHeader, List<KeyValuePair<String, String>> itemList)
            {
                String sText = String.Format("{0}{1}", Environment.NewLine, sHeader);
                itemList.ForEach(entry =>
                {
                    sText = String.Format("{0}{1} - {2}: {3}", sText, Environment.NewLine, entry.Key, entry.Value);
                });
                AppendDisplayText(logDisplaysList, sText);
            }
            public bool DrawFormattedInfoView(int? iDisplaySelector = null)
            {
                Dictionary<int, Dictionary<int, String>> displayLines = new Dictionary<int, Dictionary<int, String>>();
                String sLineIndent;
                double dFontSize;
                int iLineCount;
                int iLineCounter;
                int iColumnWidth;
                int iActIndent;
                int iLcdGroupCount;
                int iLcdGroupCounter;
                int iHeadlineCount;
                bool bResult = false;
                infoDisplaysList.ForEach(lcdSet =>
                {
                    iLcdGroupCount = lcdSet.LCDDisplayTable.Count;
                    iLcdGroupCounter = 0;
                    dFontSize = lcdSet.FontSize;
                    iLineCount = lcdSet.LineCount;
                    iLineCounter = 0;
                    iColumnWidth = 0;
                    iActIndent = 0;
                    sLineIndent = "";
                    displayLines.Clear();
                    iHeadlineCount = infoElementList.Count(element => element.Type == ElementTypes.eHeadline);
                    infoElementList.ForEach(element =>
                    {
                        if (element.Type == ElementTypes.eStructureView)
                        {
                            element.SetBody(ComputeStructureView(element.StructureData, element.StructureView, lcdSet.StructureViewOffset));
                        }
                        if (!iDisplaySelector.HasValue || iLcdGroupCounter == iDisplaySelector.Value)
                        {
                            if (element.OutputHeader != null)
                            {
                                iLineCounter = AddDisplayLine(displayLines, iLcdGroupCounter, iLineCounter, element.OutputHeader, sLineIndent);
                            }
                            element.OutputBody?.ForEach(body =>
                            {
                                iLineCounter = AddDisplayLine(displayLines, iLcdGroupCounter, iLineCounter, body, sLineIndent);
                            });
                            if (element.Type != ElementTypes.eBar & element.Type != ElementTypes.eHeadline)
                            {
                                iLineCounter = AddDisplayLine(displayLines, iLcdGroupCounter, iLineCounter, "", sLineIndent);
                            }
                        }
                        else { iLineCounter += element.LineCount; }
                        iColumnWidth = Math.Max(element.MaxWidth, iColumnWidth);
                        if (iLineCounter >= iLineCount)
                        {
                            if (iLcdGroupCounter < (iLcdGroupCount - 1)) { iLcdGroupCounter++; }
                            else
                            {
                                iLcdGroupCounter = 0;
                                iActIndent += Convert.ToInt32(Math.Round(dFontSize * iColumnWidth));
                                sLineIndent = String.Format("<indent={0}px>", iActIndent);
                                iColumnWidth = 0;
                            }
                            iLineCounter = (iLcdGroupCounter == 0) ? iHeadlineCount : 0;
                        }
                    });
                    iLcdGroupCounter = 0;
                    lcdSet.LCDDisplayTable.OrderBy(lcdGroup => lcdGroup.Key).Select(lcdGroup => lcdGroup.Value).ForEach(lcdGroup =>
                    {
                        if (!iDisplaySelector.HasValue || iLcdGroupCounter == iDisplaySelector.Value)
                        {
                            bResult = true;
                            if (displayLines.TryGetValue(iLcdGroupCounter, out Dictionary<int, String> groupLines))
                            {
                                SetDisplayText(lcdGroup, dFontSize, String.Join("", groupLines.OrderBy(line => line.Key).Select(line => line.Value)));
                            }
                            else { SetDisplayText(lcdGroup, dFontSize, ""); }
                        }
                        iLcdGroupCounter++;
                    });
                });
                return bResult;
            }
            private String ComputeHeadLine(String sName)
            {
                return String.Format("{0}{1}{2}:", sHeadlineTextFormat, HtmlColor_Headline, sName);
            }
            private String ComputeTextLine(String sText)
            {
                return String.Format("{0}{1}{2}", sDefaultTextFormat, HtmlColor_Default, sText);
            }
            private String ComputeBar(double dLevel, bool bInvers, int iSegmentCount, double dWarningLevel, double dCriticalLevel)
            {
                String sBuffer;
                if (!bInvers && dLevel >= dCriticalLevel) { sBuffer = HtmlColor_ElementCritical; }
                else if (!bInvers && dLevel >= dWarningLevel) { sBuffer = HtmlColor_ElementWarning; }
                else if (bInvers && dLevel <= dCriticalLevel) { sBuffer = HtmlColor_ElementCritical; }
                else if (bInvers && dLevel <= dWarningLevel) { sBuffer = HtmlColor_ElementWarning; }
                else { sBuffer = HtmlColor_ElementFine; }
                return String.Format("{0}{1} {2}{3:P2}",
                    sBuffer, CsRoot.Bar(dLevel, 0, 1, iSegmentCount), HtmlColor_Default, dLevel);
            }
            private String ComputeDeviceStatus(String sSymbol, String sDeviceMissingMessage, RegisteredDeviceStatusData deviceStatus)
            {
                StringBuilder outputBuffer;
                if (deviceStatus.MaxCount > 0)
                {
                    int iMaxSymbolCount = iDeviceStatusSymbolMaxCount;
                    String sColor;
                    String sOldColor = "";
                    double dActualLevel;
                    double dActiveLevel = (double)deviceStatus.ActiveCount / deviceStatus.MaxCount;
                    double dExistsLevel = (double)deviceStatus.ExistsCount / deviceStatus.MaxCount;
                    iMaxSymbolCount = Math.Min(deviceStatus.MaxCount, iMaxSymbolCount);
                    outputBuffer = new StringBuilder(String.Format("{0}{1}/{2}: ", HtmlColor_Default, deviceStatus.ActiveCount, deviceStatus.MaxCount));
                    for (int iSymbolCount = 0; iSymbolCount < iMaxSymbolCount; iSymbolCount++)
                    {
                        dActualLevel = (double)(iSymbolCount + 1) / iMaxSymbolCount;
                        if (dActualLevel <= dActiveLevel && dActiveLevel > 0) { sColor = HtmlColor_ElementFine; }
                        else if (dActualLevel <= dExistsLevel && dExistsLevel > 0) { sColor = HtmlColor_ElementWarning; }
                        else { sColor = HtmlColor_ElementCritical; }
                        if (!sColor.Equals(sOldColor)) { outputBuffer.Append(sOldColor = sColor); }
                        outputBuffer.Append(sSymbol);
                    }
                }
                else
                {
                    outputBuffer = new StringBuilder(String.Format(" {0}{1}", HtmlColor_ElementWarning, sDeviceMissingMessage));
                }
                return outputBuffer.ToString();
            }
            private List<String> ComputeStructureView(double?[][][] dStructDamageData, StructureViews eView, int iViewOffset)
            {
                List<String> drawLineList = new List<String> { "" };
                StringBuilder sOutput = new StringBuilder(sStructureView_SymbolLineSpacing);
                String sBlockSymbol;
                String sSpacing;
                String sOldSpacing = "";
                String sColor;
                String sOldColor = "";
                double?[] dDamageData;
                int iMeanCount;
                double dMeanData;
                int iFirstLoopStart;
                int iFirstLoopEnd;
                int iFirstLoopStep;
                int iSecondLoopStart;
                int iSecondLoopEnd;
                int iXMiddle;
                bool bFirstLoopForward;
                bool bFixBlock;

                if (dStructDamageData == null || dStructDamageData.Length == 0 || 
                    dStructDamageData[0].Length == 0 || dStructDamageData[0][0].Length == 0) { return drawLineList; }
                switch (eView)
                {
                    case StructureViews.eTopView:
                        bFixBlock = true;
                        iFirstLoopStart = 0;
                        iFirstLoopEnd = dStructDamageData.Length - 1;
                        iFirstLoopStep = +1;
                        bFirstLoopForward = true;
                        iSecondLoopStart = 0;
                        iSecondLoopEnd = dStructDamageData[0][0].Length - 1;
                        iXMiddle = 0;
                        break;
                    case StructureViews.eDeckView:
                        bFixBlock = false;
                        iFirstLoopStart = dStructDamageData[0].Length - 1;
                        iFirstLoopEnd = 0;
                        iFirstLoopStep = -1;
                        bFirstLoopForward = false;
                        iSecondLoopStart = 0;
                        iSecondLoopEnd = dStructDamageData[0][0].Length - 1;
                        iXMiddle = (int)(dStructDamageData.Length / 2);
                        if (dStructDamageData.ElementAtOrDefault(iXMiddle + iViewOffset) != null) { iXMiddle += iViewOffset; }
                        break;
                    default: 
                        return drawLineList;
                }
                for (int iPos1 = iFirstLoopStart; bFirstLoopForward ? iPos1 <= iFirstLoopEnd : iPos1 >= iFirstLoopEnd; iPos1 += iFirstLoopStep)
                {
                    for (int iPos2 = iSecondLoopStart; iPos2 <= iSecondLoopEnd; iPos2++)
                    {
                        switch (eView)
                        {
                            case StructureViews.eTopView: dDamageData = dStructDamageData[iPos1].Select(array => array[iPos2]).ToArray(); break;
                            case StructureViews.eDeckView: dDamageData = dStructDamageData.Select(array => array[iPos1][iPos2]).ToArray(); break;
                            default: return drawLineList;
                        }
                        iMeanCount = dDamageData.Count(value => value != null);
                        if (iMeanCount > 0)
                        {
                            dMeanData = dDamageData.Sum().Value / iMeanCount;
                            if (dMeanData > dCompareLevel_Damage_Critical) { sColor = HtmlColor_ElementCritical; }
                            else if (dMeanData > dCompareLevel_Damage_Warning) { sColor = HtmlColor_ElementWarning; }
                            else { sColor = HtmlColor_Default; }
                            if (!sColor.Equals(sOldColor)) { sOutput.Append(sOldColor = sColor); }
                            if (bFixBlock || dDamageData[iXMiddle] != null) {
                                sSpacing = sStructureView_FullBlockSpacing;
                                sBlockSymbol = sStructureView_FullBlockSymbol;
                            }
                            else {
                                sSpacing = sStructureView_InnerBlockSpacing;
                                sBlockSymbol = sStructureView_InnerBlockSymbol;
                            }
                        }
                        else
                        {
                            sSpacing = sStructureView_NoBlockSpacing;
                            sBlockSymbol = sStructureView_NoBlockSymbol;
                        }
                        if (!sSpacing.Equals(sOldSpacing)) { sOutput.Append(sOldSpacing = sSpacing); }
                        sOutput.Append(sBlockSymbol);
                    }
                    drawLineList.Add(sOutput.ToString());
                    sOutput.Clear();
                }
                return drawLineList;
            }
            private void SetDisplayText(DisplaySet lcdSet, String sText)
            {
                lcdSet.LCDDisplayTable.ForEach(lcdGroup =>
                {
                    SetDisplayText(lcdGroup.Value, lcdSet.FontSize, sText);
                });
            }
            private void SetDisplayText(List<ILcd> lcdList, double dFontSize, String sText)
            {
                String sCombinedText = String.Format("<size={0:0.0}>{1}", dFontSize, sText);
                lcdList?.ForEach(lcd =>
                {
                    lcd?.SetText(sCombinedText);
                });
            }
            private void AppendDisplayText(List<DisplaySet> lcdSets, String sText)
            {
                lcdSets?.ForEach(lcdSet =>
                {
                    lcdSet?.LCDDisplayTable?.ForEach(lcdTable =>
                    {
                        AppendDisplayText(lcdTable.Value, sText);
                    });
                });
            }
            private void AppendDisplayText(List<ILcd> lcdList, String sText)
            {
                lcdList?.ForEach(lcd =>
                {
                    lcd?.SetText(lcd?.GetText() + sText);
                });
            }
            private int AddDisplayLine(Dictionary<int, Dictionary<int, String>> displayLines, int iDisplayGroup, int iLine, String sLine, String sLineIndent)
            {
                if (!displayLines.TryGetValue(iDisplayGroup, out Dictionary<int, String> lineGroup))
                {
                    lineGroup = new Dictionary<int, String>();
                    displayLines.Add(iDisplayGroup, lineGroup);
                }
                if (!lineGroup.ContainsKey(iLine))
                {
                    lineGroup.Add(iLine, iLine == 0 ? "<indent=0>" : (Environment.NewLine + "<indent=0>"));
                }
                lineGroup[iLine] += sLineIndent + sLine;
                return iLine+=1;
            }
            private static int InnerLengthInHtml(String sText)
            {
                int iLength = 0;
                bool bInsideTag = false;
                for (int i = 0; i < sText.Length; i++)
                {
                    if (sText[i] == '<') { bInsideTag = true; continue; }
                    if (sText[i] == '>') { bInsideTag = false; continue; }
                    if (!bInsideTag) { iLength++; }
                }
                return iLength;
            }
            public class DisplayElement
            {
                public String OutputHeader { get; private set; }
                public List<String> OutputBody { get; private set; }
                public ElementTypes Type { get; private set; }
                public StructureViews StructureView { get; private set; }
                public double?[][][] StructureData { get; private set; }

                public int MaxWidth { get; private set; } = 0;
                public int LineCount { get; private set; } = 0;

                public DisplayElement()
                {
                    Type = ElementTypes.eBlankLine;
                    OutputHeader = null;
                    OutputBody = null;
                    StructureView = StructureViews.eNotDefined;
                    StructureData = null;
                }
                public DisplayElement(ElementTypes eElementType, String sOutputHeader, params String[] outputBodies)
                {
                    Type = eElementType;
                    OutputHeader = sOutputHeader;
                    OutputBody = outputBodies?.ToList();
                    StructureView = StructureViews.eNotDefined;
                    StructureData = null;
                    ComputeWidth();
                    ComputeLineCount();
                }
                public DisplayElement(String sOutputHeader, double?[][][] dStructDamageData, StructureViews eView)
                {
                    Type = ElementTypes.eStructureView;
                    OutputHeader = sOutputHeader;
                    OutputBody = null;
                    StructureView = eView;
                    StructureData = dStructDamageData;
                }
                
                private void ComputeWidth()
                {
                    double dFactor;
                    switch (Type)
                    {
                        // das geht besser mit einer mono-space-font oder wenn die Api eine font-breiten-berechnung zulässt
                        case ElementTypes.eBar: dFactor = dLineWidthCorrFactor_Bar; break;
                        case ElementTypes.eTableInfo: dFactor = dLineWidthCorrFactor_TableInfo; break;
                        case ElementTypes.eState: dFactor = dLineWidthCorrFactor_State; break;
                        case ElementTypes.eStructureView: dFactor = dLineWidthCorrFactor_StructureView; break;
                        case ElementTypes.eHeadline:
                        case ElementTypes.eSimpleInfo: dFactor = dLineWidthCorrFactor_SimpleInfo; break;
                        default: dFactor = dLineWidthCorrFactor_Default; break;
                    }
                    MaxWidth = (int)(InnerLengthInHtml(OutputHeader) * dFactor);
                    OutputBody?.ForEach(body =>
                    {
                        MaxWidth = Math.Max(MaxWidth, (int)(InnerLengthInHtml(body) * dFactor));
                    });
                }
                private void ComputeLineCount()
                {
                    LineCount = OutputBody != null ? OutputBody.Count : 0;
                    if (Type != ElementTypes.eBar & Type != ElementTypes.eHeadline) { LineCount += 2; } else { LineCount += 1; };
                }
                public void SetBody(List<String> outputBody)
                {
                    OutputBody = outputBody;
                    ComputeWidth();
                    ComputeLineCount();
                }
            }
            private class DisplaySet
            {
                public Dictionary<int, List<ILcd>> LCDDisplayTable { get; }
                public double FontSize { get; }
                public int LineCount { get; }
                public int StructureViewOffset { get; }
                public DisplaySet(Dictionary<int, List<ILcd>> displayTable, double dDefaultFontSize, double? dFontSize, int? iLineCount, int? iStruvctureViewOffset)
                {
                    LCDDisplayTable = displayTable;
                    if (dFontSize.HasValue) { FontSize = dFontSize.Value; }
                    else { FontSize = dDefaultFontSize; }
                    if (iLineCount.HasValue) { LineCount = iLineCount.Value; } 
                    else { LineCount = int.MaxValue; }
                    if (iStruvctureViewOffset.HasValue) { StructureViewOffset = iStruvctureViewOffset.Value; }
                    else { StructureViewOffset = 0; }
                }
            }
            public class RegisteredDeviceStatusData
            {
                public String DeviceGroupName { get; private set; }
                public int MaxCount { get; private set; }
                public int ExistsCount { get; private set; }
                public int ActiveCount { get; private set; }
                public RegisteredDeviceStatusData(String sDeviceGroupName)
                {
                    DeviceGroupName = sDeviceGroupName;
                    MaxCount = 0;
                    ExistsCount = 0;
                    ActiveCount = 0;
                }
                public void SetExistsCount(int iExistsCount)
                {
                    ExistsCount = iExistsCount;
                    MaxCount = Math.Max(ExistsCount, MaxCount);
                }
                public void SetActiveCount(int iActiveCount)
                {
                    ActiveCount = iActiveCount;
                }
            }
            public enum ElementTypes
            {
                eHeadline,
                eBar,
                eState,
                eSimpleInfo,
                eTableInfo,
                eStructureView,
                eBlankLine,
            }
            public enum StructureViews
            {
                eNotDefined,
                eTopView,
                eDeckView
            }
        }
    }
    namespace Locales
    {
        public static class Locales
        {
            public enum Language
            {
                deDE,
                enGB
            }
            public enum Key
            {
                ItemLanguage,
                SettingsTableDeviceName,

                Headline_Main_CargoSorter,
                Headline_Main_StatusL,
                Headline_Main_StatusS,
                Headline_Main_StatBox,
                Headline_Main_StatCpu,
                Headline_Main_StatWpn,
                Headline_Main_StatThr,
                Headline_Main_StatDev,
                Headline_Main_StatDock,
                Headline_Main_BoxFill,
                Headline_Main_BoxPurge,
                Headline_Main_StatHll,

                Headline_CargoSorter_CargoBar,
                Headline_CargoSorter_ResupplyBar,
                Headline_CargoSorter_Table_ItemsMissing,

                Headline_StatusL_FuelBar,
                Headline_StatusL_OxygenBar,
                Headline_StatusL_PentaxidBar,
                Headline_StatusL_PowerBar,
                Headline_StatusL_ShieldBar,
                Headline_StatusL_DamageBar,
                Headline_StatusL_AmmoBar,
                Headline_StatusL_CargoBar,
                Headline_StatusL_ShieldState,
                Headline_StatusL_TurretState,
                Headline_StatusL_ThrusterState,
                Headline_StatusL_OriginInfo,
                Headline_StatusL_DockingInfo,
                Headline_StatusL_PassengerInfo,

                Headline_StatusS_OriginInfo,
                Headline_StatusS_FuelBar,
                Headline_StatusS_OxygenBar,
                Headline_StatusS_PentaxidBar,
                Headline_StatusS_AmmoBar,
                Headline_StatusS_ShieldBar,

                Headline_BoxFill_Table_VesselsRemaining,
                Headline_BoxFill_Table_ItemsMissing,
                Headline_BoxFill_Table_VesselsRejected,

                Headline_BoxPurge_Table_VesselsRemaining,
                Headline_BoxPurge_Table_VesselsRejected,

                Headline_StatCpu_Table_States,

                Headline_StatDev_Table_DamagedDevices,

                Headline_StatDock_Table_DockedVessels,

                Headline_StatWpn_State_WeaponMinigunS,
                Headline_StatWpn_State_WeaponRailgunS,
                Headline_StatWpn_State_WeaponPlasmaS,
                Headline_StatWpn_State_WeaponLaserS,
                Headline_StatWpn_State_WeaponRocketS,
                Headline_StatWpn_State_TurretMinigunS,
                Headline_StatWpn_State_TurretPlasmaS,
                Headline_StatWpn_State_TurretRocketS,
                Headline_StatWpn_State_TurretArtyS,
                Headline_StatWpn_State_WeaponLaserL,
                Headline_StatWpn_State_WeaponRocketL,
                Headline_StatWpn_State_TurretSentryL,
                Headline_StatWpn_State_TurretMinigunL,
                Headline_StatWpn_State_TurretCannonL,
                Headline_StatWpn_State_TurretFlakL,
                Headline_StatWpn_State_TurretPlasmaL,
                Headline_StatWpn_State_TurretLaserL,
                Headline_StatWpn_State_TurretRocketL,
                Headline_StatWpn_State_TurretArtyL,

                Headline_StatThr_State_HoverS,
                Headline_StatThr_State_ThrusterS,
                Headline_StatThr_State_RcsS,
                Headline_StatThr_State_ThrusterL,
                Headline_StatThr_State_RcsL,

                Headline_StatHll_TopView,
                Headline_StatHll_DeckView,

                Headline_ErrorTable_FaultyParameter,
                Headline_ErrorTable_UnknownBoxes,
                Headline_ErrorTable_UnknownItems,

                Text_StandBy,
                Text_Unknown,

                Text_EntityType_LongName_HV,
                Text_EntityType_LongName_SV,
                Text_EntityType_LongName_CV,
                Text_EntityType_LongName_BA,
                Text_EntityType_ShortName_HV,
                Text_EntityType_ShortName_SV,
                Text_EntityType_ShortName_CV,
                Text_EntityType_ShortName_BA,

                Text_StatusL_OriginItemName_System,
                Text_StatusL_OriginItemName_Place,
                Text_StatusL_OriginItemName_Class,
                Text_StatusL_OriginItemName_PVP,
                Text_StatusL_OriginItemName_SystemPosition,
                Text_StatusL_OriginItemName_Position,
                Text_StatusL_OriginItemText_PVPActive,
                Text_StatusL_OriginItemText_PVPInactive,

                Text_StatusS_OriginItemName_Place,
                Text_StatusS_OriginItemName_Class,
                Text_StatusS_OriginItemName_PVP,
                Text_StatusS_OriginItemName_Position,
                Text_StatusS_OriginItemText_PVPActive,
                Text_StatusS_OriginItemText_PVPInactive,

                Text_BoxFill_VesselProgressText,

                Text_BoxPurge_VesselProgressText,

                Text_StatCpu_State_FullFail,
                Text_StatCpu_State_PartialFail,
                Text_StatCpu_State_NoFail,
                Text_StatCpu_State_Init,

                Text_StatDev_State_FullFail,
                Text_StatDev_State_NoFail,

                Text_StatDock_State_NoVessel,
                Text_StatDock_State_Off,
                Text_StatDock_State_On,
                Text_StatDock_ItemText_Energy,
                Text_StatDock_ItemText_SizeClass,

                Text_ErrorMessage_Unknown,
                Text_ErrorMessage_SettingsTableMissing,
                Text_ErrorMessage_DeviceNotFound,
                Text_ErrorMessage_ParameterNotFound,
                Text_ErrorMessage_ConverterNotFound,
                Text_ErrorMessage_ParameterNotConvertable,
                Text_ErrorMessage_NotFound,
                Text_ErrorMessage_IsOffline,
                Text_ErrorMessage_OutputContainerNotFound,
                Text_ErrorMessage_InputContainerNotFound,
                Text_ErrorMessage_SafetyStockContainerNotFound,

                Text_ErrorMessage_StatusL_ShieldMissing,
                Text_ErrorMessage_StatusL_TurretMissing,
                Text_ErrorMessage_StatusL_ThrusterMissing,

                Text_ErrorMessage_BoxFill_PriorityMissing,
                Text_ErrorMessage_BoxFill_ManagerTransferSwitchOff,
                Text_ErrorMessage_BoxFill_RemoteTransferSwitchOff,

                Text_ErrorMessage_BoxPurge_PriorityMissing,
                Text_ErrorMessage_BoxPurge_ManagerTransferSwitchOff,
                Text_ErrorMessage_BoxPurge_RemoteTransferSwitchOff,

                Text_CpuLog_FinishedSettingsReadIn,
                Text_CpuLog_FinishedDataReadIn,
                Text_CpuLog_FinishedDataComputation,
                Text_CpuLog_FinishedInfoPanelDraw,
                Text_CpuLog_FinishedFluidRefill,
                Text_CpuLog_FinishedSafetyStockRefill,
                Text_CpuLog_FinishedCargoSorting,
                Text_CpuLog_FinishedCargoTransfer,
                Text_CpuLog_FinishedDataReset,
                Text_CpuLog_FinishedComputingXFromY,
                Text_CpuLog_FinishedStepXFromY,

                Text_CpuLog_NoItemStructureFile,
                Text_CpuLog_NoInfoPanelFound,

                Symbol_StatusL_ShieldState,
                Symbol_StatusL_TurretState,
                Symbol_StatusL_ThrusterState,

                Symbol_StatWpn_TurretState,
                Symbol_StatWpn_WeaponState,

                Symbol_StatThr_HoverState,
                Symbol_StatThr_ThrusterSState,
                Symbol_StatThr_ThrusterLState,
                Symbol_StatThr_RcsSState,
                Symbol_StatThr_RcsLState,

                Symbol_StatHll_FullBlock,
                Symbol_StatHll_InnerBlock,
                Symbol_StatHll_NoBlock,
                Symbol_StatHll_BlockLineSpacing,
                Symbol_StatHll_FullBlockSpacing,
                Symbol_StatHll_InnerBlockSpacing,
                Symbol_StatHll_NoBlockSpacing
            }
            private static readonly Dictionary<Language, Dictionary<Key, String>> Table = new Dictionary<Language, Dictionary<Key, string>>{
            { Language.deDE, new Dictionary<Key, String>{
                { Key.ItemLanguage, "Deutsch" },
                { Key.SettingsTableDeviceName, "Frachtverwaltung" },

                { Key.Headline_Main_CargoSorter, "Frachtverarbeitung" },
                { Key.Headline_Main_StatusL, "Detail Status Übersicht" },
                { Key.Headline_Main_StatusS, "Kompakt Status Übersicht" },
                { Key.Headline_Main_StatBox, "Fracht Container Übersicht" },
                { Key.Headline_Main_StatCpu, "Prozessor Überwachung" },
                { Key.Headline_Main_StatWpn, "Waffen Übersicht" },
                { Key.Headline_Main_StatThr, "Triebwerks Übersicht" },
                { Key.Headline_Main_StatDev, "Geräte Überwachung" },
                { Key.Headline_Main_StatDock, "Dock Übersicht" },
                { Key.Headline_Main_BoxFill, "Frachtzuteilung" },
                { Key.Headline_Main_BoxPurge, "Frachtabholung" },
                { Key.Headline_Main_StatHll, "Strukturzustand" },

                { Key.Headline_CargoSorter_CargoBar, "Belegte Frachtkapazität" },
                { Key.Headline_CargoSorter_ResupplyBar, "Versorgungsfortschritt" },
                { Key.Headline_CargoSorter_Table_ItemsMissing, "Fehlende Versorgungsgüter" },

                { Key.Headline_StatusL_FuelBar, "Treibstoff" },
                { Key.Headline_StatusL_OxygenBar, "Sauerstoff" },
                { Key.Headline_StatusL_PentaxidBar, "Pentaxid" },
                { Key.Headline_StatusL_PowerBar, "Energielevel" },
                { Key.Headline_StatusL_ShieldBar, "Schildlevel" },
                { Key.Headline_StatusL_DamageBar, "Strukturschaden" },
                { Key.Headline_StatusL_AmmoBar, "Munition" },
                { Key.Headline_StatusL_CargoBar, "Belegte Frachtkapazität" },
                { Key.Headline_StatusL_ShieldState, "Schildstatus" },
                { Key.Headline_StatusL_TurretState, "Geschuetzstatus" },
                { Key.Headline_StatusL_ThrusterState, "Triebwerksstatus" },
                { Key.Headline_StatusL_OriginInfo, "Umgebungsdaten" },
                { Key.Headline_StatusL_DockingInfo, "Dock-Übersicht" },
                { Key.Headline_StatusL_PassengerInfo, "Passagieranzahl" },

                { Key.Headline_StatusS_OriginInfo, "Umgebungsdaten" },
                { Key.Headline_StatusS_FuelBar, "Treibstoff" },
                { Key.Headline_StatusS_OxygenBar, "Sauerstoff" },
                { Key.Headline_StatusS_PentaxidBar, "Pentaxid" },
                { Key.Headline_StatusS_AmmoBar, "Munition" },
                { Key.Headline_StatusS_ShieldBar, "Schildlevel" },

                { Key.Headline_BoxFill_Table_VesselsRemaining, "Ausstehende Beladungsvorgänge" },
                { Key.Headline_BoxFill_Table_ItemsMissing, "Unzureichende Frachtbereitstellung" },
                { Key.Headline_BoxFill_Table_VesselsRejected, "Abgelehnte anfragen" },

                { Key.Headline_BoxPurge_Table_VesselsRemaining, "Ausstehende Löschungsvorgänge" },
                { Key.Headline_BoxPurge_Table_VesselsRejected, "Abgelehnte anfragen" },

                { Key.Headline_StatCpu_Table_States, "Betriebszustand" },

                { Key.Headline_StatDev_Table_DamagedDevices, "Beschädigungen" },

                { Key.Headline_StatDock_Table_DockedVessels, "Angedockte Fahrzeuge" },

                { Key.Headline_StatWpn_State_WeaponMinigunS, "Minigunkanonen" },
                { Key.Headline_StatWpn_State_WeaponRailgunS, "Schienenkanonen" },
                { Key.Headline_StatWpn_State_WeaponPlasmaS, "Plasmakanonen" },
                { Key.Headline_StatWpn_State_WeaponLaserS, "Laserkanonen" },
                { Key.Headline_StatWpn_State_WeaponRocketS, "Raketenwerfer" },
                { Key.Headline_StatWpn_State_TurretMinigunS, "Minigun Geschuetze" },
                { Key.Headline_StatWpn_State_TurretPlasmaS, "Plasma Geschuetze" },
                { Key.Headline_StatWpn_State_TurretRocketS, "Raketen Geschuetze" },
                { Key.Headline_StatWpn_State_TurretArtyS, "Artillerie Geschuetze" },
                { Key.Headline_StatWpn_State_WeaponLaserL, "Laserkanonen" },
                { Key.Headline_StatWpn_State_WeaponRocketL, "Raketenwerfer" },
                { Key.Headline_StatWpn_State_TurretSentryL, "Wach Geschuetze" },
                { Key.Headline_StatWpn_State_TurretMinigunL, "Minigun Geschuetze" },
                { Key.Headline_StatWpn_State_TurretCannonL, "Kanonen Geschuetze" },
                { Key.Headline_StatWpn_State_TurretFlakL, "Flugabwehr Geschuetze" },
                { Key.Headline_StatWpn_State_TurretPlasmaL, "Plasma Geschuetze" },
                { Key.Headline_StatWpn_State_TurretLaserL, "Laser Geschuetze" },
                { Key.Headline_StatWpn_State_TurretRocketL, "Raketen Geschuetze" },
                { Key.Headline_StatWpn_State_TurretArtyL, "Artillerie Geschuetze" },

                { Key.Headline_StatThr_State_HoverS, "Gleitpads" },
                { Key.Headline_StatThr_State_ThrusterS, "Triebwerke" },
                { Key.Headline_StatThr_State_RcsS, "Drehmomentmodule" },
                { Key.Headline_StatThr_State_ThrusterL, "Triebwerke" },
                { Key.Headline_StatThr_State_RcsL, "Drehmomentmodule" },

                { Key.Headline_ErrorTable_FaultyParameter, "Fehlerhafte Parameter" },
                { Key.Headline_ErrorTable_UnknownBoxes, "Unbekannte Container" },
                { Key.Headline_ErrorTable_UnknownItems, "Unbekannte Gegenstände" },

                { Key.Headline_StatHll_TopView, "Draufsicht" },
                { Key.Headline_StatHll_DeckView, "Decks-Ansicht" },

                { Key.Text_StandBy, "...Standby..." },
                { Key.Text_Unknown, "Unbekannt" },

                { Key.Text_EntityType_LongName_HV, "Gleiter" },
                { Key.Text_EntityType_LongName_SV, "Kleinschiff" },
                { Key.Text_EntityType_LongName_CV, "Grossschiff" },
                { Key.Text_EntityType_LongName_BA, "Basis" },
                { Key.Text_EntityType_ShortName_HV, "HV" },
                { Key.Text_EntityType_ShortName_SV, "SV" },
                { Key.Text_EntityType_ShortName_CV, "CV" },
                { Key.Text_EntityType_ShortName_BA, "BA" },

                { Key.Text_StatusL_OriginItemName_System, "System" },
                { Key.Text_StatusL_OriginItemName_Place, "Ort" },
                { Key.Text_StatusL_OriginItemName_Class, "Klasse" },
                { Key.Text_StatusL_OriginItemName_PVP, "Polit.-Lage" },
                { Key.Text_StatusL_OriginItemName_SystemPosition, "System-Koordinaten" },
                { Key.Text_StatusL_OriginItemName_Position, "Orts-Koordinaten" },
                { Key.Text_StatusL_OriginItemText_PVPActive, "Umkämpft" },
                { Key.Text_StatusL_OriginItemText_PVPInactive, "Friedlich" },

                { Key.Text_StatusS_OriginItemName_Place, "Ort" },
                { Key.Text_StatusS_OriginItemName_Class, "Klasse" },
                { Key.Text_StatusS_OriginItemName_PVP, "Polit.-Lage" },
                { Key.Text_StatusS_OriginItemName_Position, "Koords" },
                { Key.Text_StatusS_OriginItemText_PVPActive, "Umkämpft" },
                { Key.Text_StatusS_OriginItemText_PVPInactive, "Friedlich" },

                { Key.Text_BoxFill_VesselProgressText, "beladen zu" },

                { Key.Text_BoxPurge_VesselProgressText, "entladen zu" },

                { Key.Text_StatCpu_State_FullFail, "Totalausfall" },
                { Key.Text_StatCpu_State_PartialFail, "Stoerung" },
                { Key.Text_StatCpu_State_NoFail, "In Betrieb" },
                { Key.Text_StatCpu_State_Init, "initialisiere..." },

                { Key.Text_StatDev_State_FullFail, "Zerstört" },
                { Key.Text_StatDev_State_NoFail, "Keine" },

                { Key.Text_StatDock_State_NoVessel, "Keine" },

                { Key.Text_StatDock_State_Off, "Inaktiv" },
                { Key.Text_StatDock_State_On, "Aktiv" },
                { Key.Text_StatDock_ItemText_Energy, "Energie" },
                { Key.Text_StatDock_ItemText_SizeClass, "Klasse" },

                { Key.Text_ErrorMessage_Unknown, "Undefinierter Fehler" },
                { Key.Text_ErrorMessage_SettingsTableMissing, "Frachtverwaltungstabelle nicht gefunden" },
                { Key.Text_ErrorMessage_DeviceNotFound, "Gerät in Struktur nicht auffindbar" },
                { Key.Text_ErrorMessage_ParameterNotFound, "Parameter nicht in Tabelle enthalten" },
                { Key.Text_ErrorMessage_ConverterNotFound, "Kein Datenkonverter gefunden" },
                { Key.Text_ErrorMessage_ParameterNotConvertable, "Wert nicht verarbeitbar" },
                { Key.Text_ErrorMessage_NotFound, "nicht gefunden" },
                { Key.Text_ErrorMessage_IsOffline, "ist abgeschaltet" },
                { Key.Text_ErrorMessage_OutputContainerNotFound, "Keine Ausgabe Container gefunden" },
                { Key.Text_ErrorMessage_InputContainerNotFound, "Keine Annahme Container gefunden" },
                { Key.Text_ErrorMessage_SafetyStockContainerNotFound, "Kein SafetyStock Container gefunden" },

                { Key.Text_ErrorMessage_StatusL_ShieldMissing, "Kein Schildemitter gefunden" },
                { Key.Text_ErrorMessage_StatusL_TurretMissing, "Kein Waffenturm gefunden" },
                { Key.Text_ErrorMessage_StatusL_ThrusterMissing, "Kein Triebwerk gefunden" },

                { Key.Text_ErrorMessage_BoxFill_PriorityMissing, "Keine gültige Prioritätsanmeldung" },
                { Key.Text_ErrorMessage_BoxFill_ManagerTransferSwitchOff, "Frachtzuteilung abgeschaltet" },
                { Key.Text_ErrorMessage_BoxFill_RemoteTransferSwitchOff, "Frachtzuweisung abgelehnt" },

                { Key.Text_ErrorMessage_BoxPurge_PriorityMissing, "Keine gültige Prioritätsanmeldung" },
                { Key.Text_ErrorMessage_BoxPurge_ManagerTransferSwitchOff, "Frachtannahme abgeschaltet" },
                { Key.Text_ErrorMessage_BoxPurge_RemoteTransferSwitchOff, "Frachtlöschung abgelehnt" },

                { Key.Text_CpuLog_FinishedSettingsReadIn, "Einstellungen erfolgreich geladen" },
                { Key.Text_CpuLog_FinishedDataReadIn, "Daten erfolgreich geladen" },
                { Key.Text_CpuLog_FinishedDataComputation, "Daten erfolgreich zusammengestellt" },
                { Key.Text_CpuLog_FinishedInfoPanelDraw, "Displays erfolgreich aktualisiert" },
                { Key.Text_CpuLog_FinishedFluidRefill, "Treibstoffe erfolgreich bearbeitet" },
                { Key.Text_CpuLog_FinishedSafetyStockRefill, "Container erfolgreich bearbeitet" },
                { Key.Text_CpuLog_FinishedCargoSorting, "Frachtverarbeitung abgeschlossen" },
                { Key.Text_CpuLog_FinishedCargoTransfer, "Frachttransfer abgeschlossen" },
                { Key.Text_CpuLog_FinishedDataReset, "Strukturdaten gelöscht" },
                { Key.Text_CpuLog_FinishedComputingXFromY, "Berechnungen x von y abgeschlossen" },
                { Key.Text_CpuLog_FinishedStepXFromY, "Schritt x von y abgeschlossen" },

                { Key.Text_CpuLog_NoItemStructureFile, "keine Item Gruppenstruktur Datei gefunden" },
                { Key.Text_CpuLog_NoInfoPanelFound, "kein Informations-Display gefunden" },

                { Key.Symbol_StatusL_ShieldState, "☫" },
                { Key.Symbol_StatusL_TurretState, "♖" },
                { Key.Symbol_StatusL_ThrusterState, "☉" },

                { Key.Symbol_StatWpn_TurretState, "♖" },
                { Key.Symbol_StatWpn_WeaponState, "✑" },

                { Key.Symbol_StatThr_HoverState, "▽" },
                { Key.Symbol_StatThr_ThrusterSState, "▻" },
                { Key.Symbol_StatThr_ThrusterLState, "▻" },
                { Key.Symbol_StatThr_RcsSState, "♊" },
                { Key.Symbol_StatThr_RcsLState, "♊" },

                { Key.Symbol_StatHll_FullBlock, "■" },
                { Key.Symbol_StatHll_InnerBlock, "▪" },
                { Key.Symbol_StatHll_NoBlock, " " },
                { Key.Symbol_StatHll_BlockLineSpacing, "<line-height=80%>" },
                { Key.Symbol_StatHll_FullBlockSpacing, "<cspace=0em>" },
                { Key.Symbol_StatHll_InnerBlockSpacing, "<cspace=0em>" },
                { Key.Symbol_StatHll_NoBlockSpacing, "<cspace=0.45em>" }
            }
        },
            { Language.enGB, new Dictionary<Key, String>{
                { Key.ItemLanguage, "English" },
                { Key.SettingsTableDeviceName, "Cargocontrol" },

                { Key.Headline_Main_CargoSorter, "Cargo processor:" },
                { Key.Headline_Main_StatusL, "Detailed status overview" },
                { Key.Headline_Main_StatusS, "Compact status overview" },
                { Key.Headline_Main_StatBox, "Cargo container overview" },
                { Key.Headline_Main_StatCpu, "Processor supervisor" },
                { Key.Headline_Main_StatWpn, "Weapon overview" },
                { Key.Headline_Main_StatThr, "Thruster overview" },
                { Key.Headline_Main_StatDev, "Device failure overview" },
                { Key.Headline_Main_StatDock, "Dockbay overview" },
                { Key.Headline_Main_BoxFill, "Cargo refill" },
                { Key.Headline_Main_BoxPurge, "Cargo purge" },
                { Key.Headline_Main_StatHll, "Structure Integrity" },

                { Key.Headline_CargoSorter_CargoBar, "Occupied cargo capacity" },
                { Key.Headline_CargoSorter_ResupplyBar, "Resupply progress" },
                { Key.Headline_CargoSorter_Table_ItemsMissing, "Missing supplies" },

                { Key.Headline_StatusL_FuelBar, "Fuellevel" },
                { Key.Headline_StatusL_OxygenBar, "Oxygenlevel" },
                { Key.Headline_StatusL_PentaxidBar, "Pentaxid" },
                { Key.Headline_StatusL_PowerBar, "Power level" },
                { Key.Headline_StatusL_ShieldBar, "Shield level" },
                { Key.Headline_StatusL_DamageBar, "Structure damage" },
                { Key.Headline_StatusL_AmmoBar, "Ammunition" },
                { Key.Headline_StatusL_CargoBar, "Occupied cargo capacity" },
                { Key.Headline_StatusL_ShieldState, "Shield state" },
                { Key.Headline_StatusL_TurretState, "Turret state" },
                { Key.Headline_StatusL_ThrusterState, "Thruster state" },
                { Key.Headline_StatusL_OriginInfo, "Positioning infos" },
                { Key.Headline_StatusL_DockingInfo, "Docking overview" },
                { Key.Headline_StatusL_PassengerInfo, "Passengers" },

                { Key.Headline_StatusS_OriginInfo, "Positioning infos" },
                { Key.Headline_StatusS_FuelBar, "Fuellevel" },
                { Key.Headline_StatusS_OxygenBar, "Oxygenlevel" },
                { Key.Headline_StatusS_PentaxidBar, "Pentaxid" },
                { Key.Headline_StatusS_AmmoBar, "Ammunition" },
                { Key.Headline_StatusS_ShieldBar, "Shield level" },

                { Key.Headline_BoxFill_Table_VesselsRemaining, "Remaining refill processes" },
                { Key.Headline_BoxFill_Table_ItemsMissing, "Not enough cargo" },
                { Key.Headline_BoxFill_Table_VesselsRejected, "Rejected requests" },

                { Key.Headline_BoxPurge_Table_VesselsRemaining, "Remaining purge processes" },
                { Key.Headline_BoxPurge_Table_VesselsRejected, "Rejected requests" },

                { Key.Headline_StatCpu_Table_States, "Operating mode" },

                { Key.Headline_StatDev_Table_DamagedDevices, "Defectives" },

                { Key.Headline_StatDock_Table_DockedVessels, "Docked vessels" },

                { Key.Headline_StatWpn_State_WeaponMinigunS, "Miniguns" },
                { Key.Headline_StatWpn_State_WeaponRailgunS, "Railguns" },
                { Key.Headline_StatWpn_State_WeaponPlasmaS, "Plasmaguns" },
                { Key.Headline_StatWpn_State_WeaponLaserS, "Laserguns" },
                { Key.Headline_StatWpn_State_WeaponRocketS, "Rocketlauncher" },
                { Key.Headline_StatWpn_State_TurretMinigunS, "Minigun Turrets" },
                { Key.Headline_StatWpn_State_TurretPlasmaS, "Plasma Turrets" },
                { Key.Headline_StatWpn_State_TurretRocketS, "Rocket Turrets" },
                { Key.Headline_StatWpn_State_TurretArtyS, "Longrange Turrets" },
                { Key.Headline_StatWpn_State_WeaponLaserL, "Lasergunsn" },
                { Key.Headline_StatWpn_State_WeaponRocketL, "Rocketlauncher" },
                { Key.Headline_StatWpn_State_TurretSentryL, "Sentry Turrets" },
                { Key.Headline_StatWpn_State_TurretMinigunL, "Minigun Turrets" },
                { Key.Headline_StatWpn_State_TurretCannonL, "Cannon Turrets" },
                { Key.Headline_StatWpn_State_TurretFlakL, "Anti Aircraft Turrets" },
                { Key.Headline_StatWpn_State_TurretPlasmaL, "Plasma Turrets" },
                { Key.Headline_StatWpn_State_TurretLaserL, "Laser Turrets" },
                { Key.Headline_StatWpn_State_TurretRocketL, "Rocket Turrets" },
                { Key.Headline_StatWpn_State_TurretArtyL, "Longrange Turrets" },

                { Key.Headline_StatThr_State_HoverS, "Hoverpads" },
                { Key.Headline_StatThr_State_ThrusterS, "Thrusters" },
                { Key.Headline_StatThr_State_RcsS, "Torque Modules" },
                { Key.Headline_StatThr_State_ThrusterL, "Thrusters" },
                { Key.Headline_StatThr_State_RcsL, "Torque Modules" },

                { Key.Headline_StatHll_TopView, "Top View" },
                { Key.Headline_StatHll_DeckView, "Deck View" },

                { Key.Headline_ErrorTable_FaultyParameter, "Parameters with error" },
                { Key.Headline_ErrorTable_UnknownBoxes, "Unknown container" },
                { Key.Headline_ErrorTable_UnknownItems, "Unknown items" },

                { Key.Text_StandBy, "...Standby..." },
                { Key.Text_Unknown, "Unknown" },

                { Key.Text_EntityType_LongName_HV, "Hover vessel" },
                { Key.Text_EntityType_LongName_SV, "Small ship" },
                { Key.Text_EntityType_LongName_CV, "Capital ship" },
                { Key.Text_EntityType_LongName_BA, "Base" },
                { Key.Text_EntityType_ShortName_HV, "HV" },
                { Key.Text_EntityType_ShortName_SV, "SV" },
                { Key.Text_EntityType_ShortName_CV, "CV" },
                { Key.Text_EntityType_ShortName_BA, "BA" },

                { Key.Text_StatusL_OriginItemName_System, "System" },
                { Key.Text_StatusL_OriginItemName_Place, "Place" },
                { Key.Text_StatusL_OriginItemName_Class, "Class" },
                { Key.Text_StatusL_OriginItemName_PVP, "Thread" },
                { Key.Text_StatusL_OriginItemName_SystemPosition, "System coords" },
                { Key.Text_StatusL_OriginItemName_Position, "Place coords" },
                { Key.Text_StatusL_OriginItemText_PVPActive, "War" },
                { Key.Text_StatusL_OriginItemText_PVPInactive, "Calm" },

                { Key.Text_StatusS_OriginItemName_Place, "Place" },
                { Key.Text_StatusS_OriginItemName_Class, "Class" },
                { Key.Text_StatusS_OriginItemName_PVP, "Thread" },
                { Key.Text_StatusS_OriginItemName_Position, "Coords" },
                { Key.Text_StatusS_OriginItemText_PVPActive, "War" },
                { Key.Text_StatusS_OriginItemText_PVPInactive, "Calm" },

                { Key.Text_BoxFill_VesselProgressText, "filled to" },

                { Key.Text_BoxPurge_VesselProgressText, "purged to" },

                { Key.Text_StatCpu_State_FullFail, "Fatal error" },
                { Key.Text_StatCpu_State_PartialFail, "Disruption" },
                { Key.Text_StatCpu_State_NoFail, "Running" },
                { Key.Text_StatCpu_State_Init, "initializing..." },

                { Key.Text_StatDev_State_FullFail, "Destroyed" },
                { Key.Text_StatDev_State_NoFail, "None" },

                { Key.Text_StatDock_State_NoVessel, "None" },
                { Key.Text_StatDock_State_Off, "Offline" },
                { Key.Text_StatDock_State_On, "Online" },
                { Key.Text_StatDock_ItemText_Energy, "Energy" },
                { Key.Text_StatDock_ItemText_SizeClass, "Class" },

                { Key.Text_ErrorMessage_Unknown, "Undefined Failure" },
                { Key.Text_ErrorMessage_SettingsTableMissing, "Cargocontrol table not found" },
                { Key.Text_ErrorMessage_DeviceNotFound, "Device not found in structure" },
                { Key.Text_ErrorMessage_ParameterNotFound, "Parameter not found in table" },
                { Key.Text_ErrorMessage_ConverterNotFound, "No suitable converter found" },
                { Key.Text_ErrorMessage_ParameterNotConvertable, "Parameter value not convertable" },
                { Key.Text_ErrorMessage_NotFound, "not found" },
                { Key.Text_ErrorMessage_IsOffline, "is offline" },
                { Key.Text_ErrorMessage_OutputContainerNotFound, "Output container not found" },
                { Key.Text_ErrorMessage_InputContainerNotFound, "Input container not found" },
                { Key.Text_ErrorMessage_SafetyStockContainerNotFound, "SafetyStock container not found" },

                { Key.Text_ErrorMessage_StatusL_ShieldMissing, "No shield device found" },
                { Key.Text_ErrorMessage_StatusL_TurretMissing, "No Turret found" },
                { Key.Text_ErrorMessage_StatusL_ThrusterMissing, "No thruster found" },

                { Key.Text_ErrorMessage_BoxFill_PriorityMissing, "Priority registration failed" },
                { Key.Text_ErrorMessage_BoxFill_ManagerTransferSwitchOff, "Refill service offline" },
                { Key.Text_ErrorMessage_BoxFill_RemoteTransferSwitchOff, "Refill service rejected" },

                { Key.Text_ErrorMessage_BoxPurge_PriorityMissing, "Priority registration failed" },
                { Key.Text_ErrorMessage_BoxPurge_ManagerTransferSwitchOff, "Purge service offline" },
                { Key.Text_ErrorMessage_BoxPurge_RemoteTransferSwitchOff, "Purge service rejected" },

                { Key.Text_CpuLog_FinishedSettingsReadIn, "Read-In Management-Settings successful" },
                { Key.Text_CpuLog_FinishedDataReadIn, "Read-In data successful" },
                { Key.Text_CpuLog_FinishedDataComputation, "Prepare Data successful" },
                { Key.Text_CpuLog_FinishedInfoPanelDraw, "Draw Data successful" },
                { Key.Text_CpuLog_FinishedFluidRefill, "Fuels-Refill processed" },
                { Key.Text_CpuLog_FinishedSafetyStockRefill, "Cargo-Refill processed" },
                { Key.Text_CpuLog_FinishedCargoSorting, "Cargo-Sorting successful" },
                { Key.Text_CpuLog_FinishedCargoTransfer, "Cargo-Conveying successful" },
                { Key.Text_CpuLog_FinishedDataReset, "Structure data removed" },
                { Key.Text_CpuLog_FinishedComputingXFromY, "Calculation x from y finished" },
                { Key.Text_CpuLog_FinishedStepXFromY, "Step x von y finished" },

                { Key.Text_CpuLog_NoItemStructureFile, "No item structure file found" },
                { Key.Text_CpuLog_NoInfoPanelFound, "No info panel found" },

                { Key.Symbol_StatusL_ShieldState, "☫" },
                { Key.Symbol_StatusL_TurretState, "♖" },
                { Key.Symbol_StatusL_ThrusterState, "☉" },

                { Key.Symbol_StatWpn_TurretState, "♖" },
                { Key.Symbol_StatWpn_WeaponState, "✑" },

                { Key.Symbol_StatThr_HoverState, "▽" },
                { Key.Symbol_StatThr_ThrusterSState, "▻" },
                { Key.Symbol_StatThr_ThrusterLState, "▻" },
                { Key.Symbol_StatThr_RcsSState, "♊" },
                { Key.Symbol_StatThr_RcsLState, "♊" },

                { Key.Symbol_StatHll_FullBlock, "■" },
                { Key.Symbol_StatHll_InnerBlock, "▪" },
                { Key.Symbol_StatHll_NoBlock, " " },
                { Key.Symbol_StatHll_BlockLineSpacing, "<line-height=80%>" },
                { Key.Symbol_StatHll_FullBlockSpacing, "<cspace=0em>" },
                { Key.Symbol_StatHll_InnerBlockSpacing, "<cspace=0em>" },
                { Key.Symbol_StatHll_NoBlockSpacing, "<cspace=0.45em>" }
            }}
        };
            public static String GetValue(Language lng, Key key)
            {
                return Table[lng][key];
            }
            public static String GetEntityTypeNameLong(Language lng, EntityType type)
            {
                String sName;
                switch (type)
                {
                    case EntityType.HV: sName = GetValue(lng, Key.Text_EntityType_LongName_HV); break;
                    case EntityType.SV: sName = GetValue(lng, Key.Text_EntityType_LongName_SV); break;
                    case EntityType.CV: sName = GetValue(lng, Key.Text_EntityType_LongName_CV); break;
                    case EntityType.BA: sName = GetValue(lng, Key.Text_EntityType_LongName_BA); break;
                    default: sName = GetValue(lng, Key.Text_Unknown); break;
                }
                return sName;
            }
            public static String GetEntityTypeNameShort(Language lng, EntityType type)
            {
                String sName;
                switch (type)
                {
                    case EntityType.HV: sName = GetValue(lng, Key.Text_EntityType_ShortName_HV); break;
                    case EntityType.SV: sName = GetValue(lng, Key.Text_EntityType_ShortName_SV); break;
                    case EntityType.CV: sName = GetValue(lng, Key.Text_EntityType_ShortName_CV); break;
                    case EntityType.BA: sName = GetValue(lng, Key.Text_EntityType_ShortName_BA); break;
                    default: sName = GetValue(lng, Key.Text_Unknown); break;
                }
                return sName;
            }
        }
    }
    namespace Settings
    {
        public static class Settings
        {
            public static readonly String Version = "1.0.23";
            public static readonly String Author = "Preston";

            public enum Key
            {
                VersionNumber,
                Author,

                FileName_ItemStructureTree,

                DisplayColor_Default,
                DisplayColor_Headline,
                DisplayColor_Fine,
                DisplayColor_Warning,
                DisplayColor_Critical,

                DisplayColor_White,
                DisplayColor_Grey,
                DisplayColor_Black,
                DisplayColor_Yellow,
                DisplayColor_Red,
                DisplayColor_Green,
                DisplayColor_Blue,
                DisplayColor_Purple,

                TextFormat_Default,
                TextFormat_Headline,
                TextFormat_BarSegmentCount,
                TextFormat_DeviceStatusMaxSymbolCount,

                FontSize_DefaultInfo,
                FontSize_DefaultLog,

                CompareLevel_Power_Warning,
                CompareLevel_Power_Critical,
                CompareLevel_Shield_Warning,
                CompareLevel_Shield_Critical,
                CompareLevel_Damage_Warning,
                CompareLevel_Damage_Critical,
                CompareLevel_BoxFill_Warning,
                CompareLevel_BoxFill_Critical,
                CompareLevel_BoxEmpty_Warning,
                CompareLevel_BoxEmpty_Critical,
                CompareLevel_TanksEmpty_Warning,
                CompareLevel_TanksEmpty_Critical,

                TickCount_CpuInfCpu_FailsToNotify,
                TickCount_CpuInfHll_LayersPerTick,

                TickCount_ItemsToSort,
                TickCount_VesselsToFill,
                TickCount_ItemsToFill,
                TickCount_VesselsToPurge,
                TickCount_ItemsToPurge,

                LineWidthCorrFactor_Bar,
                LineWidthCorrFactor_TableInfo,
                LineWidthCorrFactor_SimpleInfo,
                LineWidthCorrFactor_State,
                LineWidthCorrFactor_StructureView,
                LineWidthCorrFactor_Default

            }
            private static readonly Dictionary<Key, String> Table = new Dictionary<Key, string>
            {
                { Key.VersionNumber, Version },
                { Key.Author, Author },

                { Key.FileName_ItemStructureTree, "ItemStructureTree.ecf" },
                
                { Key.DisplayColor_Default, "<color=white>" },
                { Key.DisplayColor_Headline, "<color=grey>" },
                { Key.DisplayColor_Fine, "<color=green>" },
                { Key.DisplayColor_Warning, "<color=yellow>" },
                { Key.DisplayColor_Critical, "<color=red>" },

                { Key.DisplayColor_White, "<color=white>" },
                { Key.DisplayColor_Grey, "<color=grey>" },
                { Key.DisplayColor_Black, "<color=black>" },
                { Key.DisplayColor_Yellow, "<color=yellow>" },
                { Key.DisplayColor_Red, "<color=red>" },
                { Key.DisplayColor_Green, "<color=green>" },
                { Key.DisplayColor_Blue, "<color=blue>" },
                { Key.DisplayColor_Purple, "<color=purple>" },

                { Key.TextFormat_Default, "<line-height=100%><cspace=0em>" },
                { Key.TextFormat_Headline, "<line-height=100%><cspace=0em>" },
                { Key.TextFormat_BarSegmentCount, "15" },
                { Key.TextFormat_DeviceStatusMaxSymbolCount, "10" },

                { Key.FontSize_DefaultInfo, "4" },
                { Key.FontSize_DefaultLog, "3" },

                { Key.CompareLevel_Power_Warning, "0,75" },
                { Key.CompareLevel_Power_Critical, "0,9" },
                { Key.CompareLevel_Shield_Warning, "0,4" },
                { Key.CompareLevel_Shield_Critical, "0,2" },
                { Key.CompareLevel_Damage_Warning, "0,5" },
                { Key.CompareLevel_Damage_Critical, "0,75" },
                { Key.CompareLevel_BoxFill_Warning, "0,65" },
                { Key.CompareLevel_BoxFill_Critical, "0,9" },
                { Key.CompareLevel_BoxEmpty_Warning, "0,35" },
                { Key.CompareLevel_BoxEmpty_Critical, "0,2" },
                { Key.CompareLevel_TanksEmpty_Warning, "0,25" },
                { Key.CompareLevel_TanksEmpty_Critical, "0,1" },

                { Key.TickCount_CpuInfCpu_FailsToNotify, "3" },
                { Key.TickCount_CpuInfHll_LayersPerTick, "50" },
                { Key.TickCount_ItemsToSort, "3" },
                { Key.TickCount_VesselsToFill, "1" },
                { Key.TickCount_ItemsToFill, "1" },
                { Key.TickCount_VesselsToPurge, "1" },
                { Key.TickCount_ItemsToPurge, "1" },

                { Key.LineWidthCorrFactor_Bar, "1,00" },
                { Key.LineWidthCorrFactor_TableInfo, "0,85" },
                { Key.LineWidthCorrFactor_SimpleInfo, "0,80" },
                { Key.LineWidthCorrFactor_State, "0,85" },
                { Key.LineWidthCorrFactor_StructureView, "0,90" },
                { Key.LineWidthCorrFactor_Default, "1,00" }
            };
            public static T GetValue<T>(Key key)
            {
                T value = default;
                String sParameter = Table[key];
                TypeConverter converter = TypeDescriptor.GetConverter(typeof(T));
                if (sParameter != null && !sParameter.Equals("") && converter != null)
                {
                    try { value = (T)converter.ConvertFromString(sParameter); }
                    catch (Exception) {
                        throw new Exception("Parameter in " + key.ToString() + " not convertible to " + typeof(T).Name);
                    }
                }
                return value;
            }
        }
    }
    
    namespace Debug
    {
        using Locales = Locales.Locales;
        using DisplayViewManager = DisplayViewManager.DisplayViewManager;
        using GenericMethods = GenericMethods.GenericMethods;
        using Settings = Settings.Settings;

        public static class BugBuster
        {
            public static void Run(IScriptModData root, Locales.Language lng)
            {
                const string sDebugProcessorName = "";
                // with no processor name debugging is off
                if (String.IsNullOrEmpty(sDebugProcessorName)) { return; }

                // Script Content Data and methods
                ICsScriptFunctions CsRoot = root.CsRoot;
                // entity (vessel) the script is executed for (cycles every vessel)
                IEntityData E = root.E;
                // playfield data the entity is on
                IPlayfieldData P = root.P;

                // without structure powered and without "processing device" the scrip should "sleep"
                IBlockData[] deviceProcessors = CsRoot.Devices(E.S, sDebugProcessorName);
                if (!E.S.IsPowerd || deviceProcessors == null || deviceProcessors.Count() < 1) { return; }

                // prepare output for debugging
                CsRoot.I18nDefaultLanguage = Locales.GetValue(lng, Locales.Key.ItemLanguage);
                DisplayViewManager displayManager = new DisplayViewManager(CsRoot, E, lng);
                displayManager.AddLogDisplays(sDebugProcessorName, Settings.Version, CsRoot.GetDevices<ILcd>(deviceProcessors));
                try
                {

                    
                    
                    


                    //E.Move(new UnityEngine.Vector3(0, 0, 0));



                }
                catch (Exception e)
                {
                    displayManager.AddLogEntry(e);
                }
            }
        }
    }
}