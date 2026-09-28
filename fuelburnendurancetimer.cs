// ============================================================
// HUD Extras: Fuel Burn Endurance Timer
// Made by Hellcat92, tiny adjustment by Appulcake
// Version: 3.2.0
// Date: 28 September 2026
// ============================================================

using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;

namespace FuelBurnHUD;

public enum RangeUnit
{
    Km,
    Nm,
    Mile
}

[BepInPlugin("com.hellcat92.fuelburnhud", "Fuel Burn Endurance Timer", "3.2.0")]
public class Plugin : BaseUnityPlugin
{
    public const int LockedHorizontalOffset = -310;
    public const int LockedVerticalOffset = -120;
    public static ConfigEntry<bool> ModEnabled;
    
    public static ConfigEntry<bool> ShowFUEL;
    public static ConfigEntry<bool> ShowTIMEREM;
    public static ConfigEntry<bool> ShowFLOW;
    public static ConfigEntry<bool> ShowRNG;
    
    public static ConfigEntry<int> FontSize;
    
    public static ConfigEntry<bool> UseMetricUnits;
    
    public static ConfigEntry<RangeUnit> RangeUnits;
    
    private void Awake()
    {
        Logger.LogInfo("Fuel Burn Endurance Timer 3.2.0 Loaded");
        
        ModEnabled = Config.Bind("General", "Enable Mod", true);
        
        ShowFUEL = Config.Bind("HUD", "Show FUEL", true);
        ShowTIMEREM = Config.Bind("HUD", "Show TIME REM", true);
        ShowFLOW = Config.Bind("HUD", "Show FLOW", true);
        ShowRNG = Config.Bind("HUD", "Show RNG", true);
        
        FontSize = Config.Bind("HUD", "Font Size", 16);
        
        UseMetricUnits = Config.Bind("HUD", "Use Metric Units", false);
        
        RangeUnits = Config.Bind("HUD", "Range Units", RangeUnit.Km);
        
        gameObject.AddComponent<FuelBurnWatcher>();
    }
}

public class FuelBurnWatcher : MonoBehaviour
{
    private Aircraft aircraft;
    
    private FuelBurnController controller;
    private CombatHUD hud;
    private Transform hudCenter;
    
    private void Update()
    {
        if (!Plugin.ModEnabled.Value)
        {
            ResetInjection();
            return;
        }
        
        var newHud = SceneSingleton<CombatHUD>.i;
        if (newHud != hud)
        {
            hud = newHud;
            ResetInjection();
        }
        
        if (hud == null)
            return;
        
        if (aircraft != hud.aircraft)
        {
            aircraft = hud.aircraft;
            ResetInjection();
        }
        
        if (aircraft == null)
            return;
        
        var fh = SceneSingleton<FlightHud>.i;
        if (fh == null)
            return;
        
        var newCenter = fh.GetHUDCenter();
        if (newCenter != hudCenter)
        {
            hudCenter = newCenter;
            ResetInjection();
        }
        
        if (hudCenter == null)
            return;
        
        if (controller == null)
            InjectHUD();
    }
    
    private void ResetInjection()
    {
        if (controller != null)
        {
            Destroy(controller.gameObject);
            controller = null;
        }
    }
    
    private void InjectHUD()
    {
        var go = new GameObject("FuelBurnHUD");
        go.transform.SetParent(hudCenter, false);
        
        controller = go.AddComponent<FuelBurnController>();
        controller.hud = hud;
        controller.aircraft = aircraft;
    }
}

public class FuelBurnController : MonoBehaviour
{
    private const float KgToLb = 2.20462262f;
    
    private static readonly FieldInfo TargetInfoField =
        typeof(CombatHUD).GetField("targetInfo", BindingFlags.Instance | BindingFlags.NonPublic);
    
    public CombatHUD hud;
    public Aircraft aircraft;
    
    private float flashTimer;
    private float flowKgPerSec;
    private TextMeshProUGUI flowText;
    
    private TextMeshProUGUI fuelText;
    private bool hasSample;
    
    private float lastFuelKg;
    private float lastTime;
    private TextMeshProUGUI rangeText;
    private TextMeshProUGUI timeRemText;
    
    private void Start()
    {
        fuelText = CreateTMP("FUEL");
        timeRemText = CreateTMP("TIME REM");
        flowText = CreateTMP("FLOW");
        rangeText = CreateTMP("RNG");
        
        var vanillaHUD = GetVanillaHUDText();
        if (vanillaHUD != null)
        {
            var vanillaFont = vanillaHUD.font;
            var vanillaMaterial = vanillaHUD.fontSharedMaterial;
            
            fuelText.font = vanillaFont;
            timeRemText.font = vanillaFont;
            flowText.font = vanillaFont;
            rangeText.font = vanillaFont;
            
            fuelText.fontSharedMaterial = vanillaMaterial;
            flowText.fontSharedMaterial = vanillaMaterial;
            rangeText.fontSharedMaterial = vanillaMaterial;
            timeRemText.fontSharedMaterial = vanillaMaterial;
        }
        
        ThemeManager.ThemeGroupChanged += ApplyThemeColour;
        ApplyThemeColour();
    }
    
    private void LateUpdate()
    {
        if (!Plugin.ModEnabled.Value)
        {
            fuelText.enabled = false;
            timeRemText.enabled = false;
            flowText.enabled = false;
            rangeText.enabled = false;
            return;
        }
        
        if (aircraft == null)
            return;
        
        var tanks = aircraft.GetFuelTanks();
        if (tanks == null || tanks.Count == 0)
            return;
        
        var totalKg = 0f;
        foreach (var t in tanks)
            if (t != null)
                totalKg += t.fuelMass;
        
        var now = Time.time;
        
        if (lastTime == 0f)
        {
            lastTime = now;
            lastFuelKg = totalKg;
            flowKgPerSec = 0f;
            hasSample = false;
        }
        
        if (now - lastTime >= 1f)
        {
            var delta = lastFuelKg - totalKg;
            var dt = now - lastTime;
            
            lastTime = now;
            lastFuelKg = totalKg;
            
            if (delta > 0.01f && dt > 0f)
            {
                flowKgPerSec = delta / dt;
                hasSample = true;
            }
            else
            {
                flowKgPerSec = 0f;
                hasSample = false;
            }
        }
        
        var enduranceSec =
            hasSample && flowKgPerSec > 0f && totalKg > 0f
                ? totalKg / flowKgPerSec
                : 0f;
        
        var baseX = Plugin.LockedHorizontalOffset;
        var baseY = Plugin.LockedVerticalOffset;
        
        fuelText.fontSize = Plugin.FontSize.Value;
        timeRemText.fontSize = Plugin.FontSize.Value;
        flowText.fontSize = Plugin.FontSize.Value;
        rangeText.fontSize = Plugin.FontSize.Value;
        
        // ⭐ Collapse‑upwards layout
        var y = baseY;
        
        if (Plugin.ShowFUEL.Value)
        {
            fuelText.enabled = true;
            fuelText.rectTransform.anchoredPosition = new Vector2(baseX, y);
            y -= 20;
        }
        else
        {
            fuelText.enabled = false;
        }
        
        if (Plugin.ShowTIMEREM.Value)
        {
            timeRemText.enabled = true;
            timeRemText.rectTransform.anchoredPosition = new Vector2(baseX, y);
            y -= 20;
        }
        else
        {
            timeRemText.enabled = false;
        }
        
        if (Plugin.ShowFLOW.Value)
        {
            flowText.enabled = true;
            flowText.rectTransform.anchoredPosition = new Vector2(baseX, y);
            y -= 20;
        }
        else
        {
            flowText.enabled = false;
        }
        
        if (Plugin.ShowRNG.Value)
        {
            rangeText.enabled = true;
            rangeText.rectTransform.anchoredPosition = new Vector2(baseX, y);
        }
        else
        {
            rangeText.enabled = false;
        }
        
        var metric = Plugin.UseMetricUnits.Value;
        
        // FUEL
        if (Plugin.ShowFUEL.Value)
        {
            if (metric)
                fuelText.text = $"FUEL [{totalKg:0}] kg";
            else
                fuelText.text = $"FUEL [{totalKg * KgToLb:0}] lb";
        }
        
        // ⭐ TIME REM — isolated critical colour logic
        if (Plugin.ShowTIMEREM.Value)
        {
            var colours = ThemeManager.Active?.ColorTheme;
            
            if (!hasSample || enduranceSec <= 0f)
            {
                timeRemText.text = "TIME REM (--:--:--)";
                if (colours != null)
                    timeRemText.color = colours.AllClear;
            }
            else
            {
                var h = Mathf.FloorToInt(enduranceSec / 3600);
                var m = Mathf.FloorToInt(enduranceSec % 3600 / 60);
                var s = Mathf.FloorToInt(enduranceSec % 60);
                timeRemText.text = $"TIME REM ({h}:{m:D2}:{s:D2})";
                
                var minutes = enduranceSec / 60f;
                
                if (colours != null)
                {
                    if (minutes <= 1f)
                    {
                        flashTimer += Time.deltaTime * 4f;
                        var flash = Mathf.FloorToInt(flashTimer) % 2 == 0;
                        timeRemText.color = flash ? colours.Alert : colours.Warning;
                    }
                    else if (minutes <= 5f)
                    {
                        timeRemText.color = colours.Alert;
                    }
                    else if (minutes <= 15f)
                    {
                        timeRemText.color = colours.Warning;
                    }
                    else
                    {
                        timeRemText.color = colours.AllClear;
                    }
                }
            }
        }
        
        // FLOW
        if (Plugin.ShowFLOW.Value)
        {
            if (!hasSample || flowKgPerSec <= 0f)
            {
                flowText.text = metric ? "FLOW [--] kg/s" : "FLOW [--] lb/s";
            }
            else
            {
                if (metric)
                    flowText.text = $"FLOW [{flowKgPerSec:0.0}] kg/s";
                else
                    flowText.text = $"FLOW [{flowKgPerSec * KgToLb:0.0}] lb/s";
            }
        }
        
        // RANGE
        if (Plugin.ShowRNG.Value)
        {
            if (!hasSample || enduranceSec <= 0f || aircraft.rb == null)
            {
                rangeText.text = "RNG ----";
            }
            else
            {
                var gs = aircraft.rb.velocity.magnitude;
                var meters = gs * enduranceSec;
                
                switch (Plugin.RangeUnits.Value)
                {
                    case RangeUnit.Km:
                        var km = meters / 1000f;
                        rangeText.text = $"RNG {km:0}km";
                        break;
                    
                    case RangeUnit.Mile:
                        var mi = meters / 1609.34f;
                        rangeText.text = $"RNG {mi:0}mi";
                        break;
                    
                    default: // Nm
                        var nm = meters / 1852f;
                        rangeText.text = $"RNG {nm:0}nm";
                        break;
                }
            }
        }
    }
    
    private void OnDestroy()
    {
        ThemeManager.ThemeGroupChanged -= ApplyThemeColour;
    }
    
    private TextMeshProUGUI GetVanillaHUDText()
    {
        if (hud == null)
            return null;
        
        return TargetInfoField?.GetValue(hud) as TextMeshProUGUI;
    }
    
    private void ApplyThemeColour()
    {
        if (ThemeManager.Active == null)
            return;
        
        var color = ThemeManager.Active.ColorTheme.AllClear;
        fuelText.color = color;
        flowText.color = color;
        rangeText.color = color;
    }
    
    private TextMeshProUGUI CreateTMP(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        
        var rt = tmp.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(800f, 40f);
        
        return tmp;
    }
}