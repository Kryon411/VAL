// VALTheme.main.js
// Unified theme manager for VAL (SoftGlass + deterministic neural overlay)
//
// Goals:
//  - Everything theme-related is controlled here (enable/disable + config + background layers).
//  - Deterministic neural topology (seeded + stable across reloads; no re-roll on resize).
//  - Theme can be toggled from Control Centre (Dock) without needing to reload.
//
// Notes:
//  - When disabled: page remains the website default (no overlay, no root class).
//  - When enabled: adds `val-theme-enabled` class to <html> and injects a fixed, pointer-events:none canvas layer.

(function () {
  const MODULE_NAME = "VALTheme";

  // Persisted keys (Dock uses the same enabled key)
  const LS_THEME_ENABLED = "VAL_ThemeEnabled";
  const LS_THEME_CONFIG  = "VAL_ThemeConfig";

  // Activity -> Idle/Active opacity swap
  const IDLE_TIMEOUT_MS = 120000; // 2 minutes
  const MODE_IDLE = "idle";
  const MODE_ACTIVE = "active";
  // Locked neural net model (fixed topology), exported from VAL_Background_Tuner_ModelExport.html.
  const LOCKED_MODEL = {
  "nodes": [
    {
      "ux": -0.5214418120664999,
      "uy": 0.008241211691978377,
      "base": 1.6227593431816607,
      "glowPhase": 4.205800219326898,
      "glowSpeed": 0.17314162445247863,
      "orbitPhase": 2.359458521413236,
      "orbitSpeed": 0.05556187135920525,
      "orbitRadius": 6.662494946066102,
      "hub": false
    },
    {
      "ux": 0.1496972037177561,
      "uy": 0.546966332027746,
      "base": 1.4517920697478437,
      "glowPhase": 4.048988116765795,
      "glowSpeed": 0.23547251949390247,
      "orbitPhase": 5.832015102535966,
      "orbitSpeed": 0.08574773878108974,
      "orbitRadius": 5.1704094279015775,
      "hub": false
    },
    {
      "ux": -0.8555265429752589,
      "uy": -0.08049807601796855,
      "base": 1.8155820114519923,
      "glowPhase": 4.365680729906143,
      "glowSpeed": 0.1775247328413956,
      "orbitPhase": 5.871130329569566,
      "orbitSpeed": 0.05326462897616266,
      "orbitRadius": 3.6166921671159438,
      "hub": false
    },
    {
      "ux": -0.8995571636283753,
      "uy": -0.1472766504019354,
      "base": 4.339026887589428,
      "glowPhase": 3.397023300651642,
      "glowSpeed": 0.16243649751708877,
      "orbitPhase": 3.2804868114315666,
      "orbitSpeed": 0.043259156265048046,
      "orbitRadius": 8.857423346030238,
      "hub": true
    },
    {
      "ux": 0.5593443150635253,
      "uy": -0.3048194660184393,
      "base": 2.165436576205538,
      "glowPhase": 10.191023333845656,
      "glowSpeed": 0.24393135999329452,
      "orbitPhase": 3.974607246725406,
      "orbitSpeed": 0.051095904839927944,
      "orbitRadius": 2.2926738980099763,
      "hub": false
    },
    {
      "ux": 0.3994797882211759,
      "uy": 0.2734565875209789,
      "base": 2.4700923263488295,
      "glowPhase": 8.631770117398538,
      "glowSpeed": 0.18143289775299457,
      "orbitPhase": 2.907234396851689,
      "orbitSpeed": 0.07776697954971919,
      "orbitRadius": 6.754884783490003,
      "hub": false
    },
    {
      "ux": 0.5500501021636796,
      "uy": 0.16253465986006188,
      "base": 1.5194220355906778,
      "glowPhase": 5.321510724048135,
      "glowSpeed": 0.17017819406762452,
      "orbitPhase": 2.2922903747892747,
      "orbitSpeed": 0.04749016439566191,
      "orbitRadius": 7.220547498717693,
      "hub": false
    },
    {
      "ux": -0.6051251834611238,
      "uy": 0.17951018409734146,
      "base": 2.310973816784376,
      "glowPhase": 8.785125264636758,
      "glowSpeed": 0.2499793451000888,
      "orbitPhase": 4.4461610196308206,
      "orbitSpeed": 0.06808752930220938,
      "orbitRadius": 6.6980857846128385,
      "hub": false
    },
    {
      "ux": -0.21801852453679443,
      "uy": 0.016586810859203452,
      "base": 1.6657122952698562,
      "glowPhase": 8.29160587198505,
      "glowSpeed": 0.1460222333199225,
      "orbitPhase": 4.797820648034906,
      "orbitSpeed": 0.060719706223357914,
      "orbitRadius": 9.397040325735304,
      "hub": false
    },
    {
      "ux": 0.5267844647256681,
      "uy": 0.11721674723172336,
      "base": 2.1681705025548896,
      "glowPhase": 4.4274848625422525,
      "glowSpeed": 0.23165962320182334,
      "orbitPhase": 2.648760208583761,
      "orbitSpeed": 0.04492108581866866,
      "orbitRadius": 5.715358761694604,
      "hub": false
    },
    {
      "ux": -0.4851228843506684,
      "uy": 0.5812339603404393,
      "base": 2.5610039907012023,
      "glowPhase": 3.5565003501211985,
      "glowSpeed": 0.1760085234690398,
      "orbitPhase": 3.9802181596403527,
      "orbitSpeed": 0.0724431574508817,
      "orbitRadius": 9.813654173860218,
      "hub": false
    },
    {
      "ux": 0.8870073229866567,
      "uy": 0.07346135974665484,
      "base": 2.3984216228840403,
      "glowPhase": 5.553396669634707,
      "glowSpeed": 0.3062824247936743,
      "orbitPhase": 1.2773473936249629,
      "orbitSpeed": 0.06730240675043941,
      "orbitRadius": 3.4615819103202305,
      "hub": false
    },
    {
      "ux": 0.19813743447156096,
      "uy": 0.45040609055753966,
      "base": 1.8201923032930718,
      "glowPhase": 6.122251536203347,
      "glowSpeed": 0.17026866420288206,
      "orbitPhase": 5.983026358429444,
      "orbitSpeed": 0.0805593120738507,
      "orbitRadius": 6.039865406540212,
      "hub": false
    },
    {
      "ux": 0.05297285531807479,
      "uy": 0.07982861625128441,
      "base": 2.209753312917992,
      "glowPhase": 11.198319212114132,
      "glowSpeed": 0.30553056035183523,
      "orbitPhase": 0.7156611804871758,
      "orbitSpeed": 0.031697436937032614,
      "orbitRadius": 4.257230070185013,
      "hub": false
    },
    {
      "ux": -0.3209678907824202,
      "uy": -0.4367913260245187,
      "base": 4.887833964378556,
      "glowPhase": 6.3202606687413425,
      "glowSpeed": 0.17811877406864118,
      "orbitPhase": 2.6983154405122995,
      "orbitSpeed": 0.059378009633045806,
      "orbitRadius": 7.65073257346518,
      "hub": true
    },
    {
      "ux": 0.627780973446714,
      "uy": 0.44360524328847123,
      "base": 1.567950112481571,
      "glowPhase": 8.584975241485505,
      "glowSpeed": 0.25517692472775944,
      "orbitPhase": 6.879203212478414,
      "orbitSpeed": 0.06467056150424502,
      "orbitRadius": 3.6433642285410137,
      "hub": false
    },
    {
      "ux": -0.30617409914403376,
      "uy": 0.7111799800123252,
      "base": 1.6965447055084137,
      "glowPhase": 4.520422316178434,
      "glowSpeed": 0.14080388151125656,
      "orbitPhase": 4.143393222328372,
      "orbitSpeed": 0.04041993657627267,
      "orbitRadius": 8.477286745742333,
      "hub": false
    },
    {
      "ux": 0.044900247020089386,
      "uy": -0.24973456581189646,
      "base": 2.2417919483184487,
      "glowPhase": 8.982237902500376,
      "glowSpeed": 0.3173078440347298,
      "orbitPhase": 5.490429259895275,
      "orbitSpeed": 0.08071299475118496,
      "orbitRadius": 9.58986902515884,
      "hub": false
    },
    {
      "ux": -0.04971376714497529,
      "uy": -0.3720961163023791,
      "base": 2.2215195917584705,
      "glowPhase": 5.44219843441398,
      "glowSpeed": 0.13522424815415132,
      "orbitPhase": 4.063779444852907,
      "orbitSpeed": 0.06497158518305073,
      "orbitRadius": 5.419086943682649,
      "hub": false
    },
    {
      "ux": 0.16813429200517635,
      "uy": 0.13888441209784702,
      "base": 2.3038709409623124,
      "glowPhase": 6.361364567639689,
      "glowSpeed": 0.2078000913539391,
      "orbitPhase": 1.7972609524051228,
      "orbitSpeed": 0.03355865691705056,
      "orbitRadius": 9.334816778443116,
      "hub": false
    },
    {
      "ux": 0.3826911754893746,
      "uy": -0.4264753477049443,
      "base": 2.5205675012003708,
      "glowPhase": 4.431667739570849,
      "glowSpeed": 0.14771672447783238,
      "orbitPhase": 2.3324544268290306,
      "orbitSpeed": 0.032236221228926026,
      "orbitRadius": 6.651613075279957,
      "hub": false
    },
    {
      "ux": 0.19972575438184673,
      "uy": -0.20049013493650752,
      "base": 1.5336917719886647,
      "glowPhase": 9.650794753057198,
      "glowSpeed": 0.2918784616620316,
      "orbitPhase": 2.197602416432913,
      "orbitSpeed": 0.034474314832273686,
      "orbitRadius": 3.137135944348846,
      "hub": false
    },
    {
      "ux": -0.20583121710043167,
      "uy": 0.43584935211500364,
      "base": 1.5645392633199724,
      "glowPhase": 8.267010683785363,
      "glowSpeed": 0.1335095083606766,
      "orbitPhase": 3.69539360767713,
      "orbitSpeed": 0.08710434252559056,
      "orbitRadius": 2.248682650524918,
      "hub": false
    },
    {
      "ux": 0.5588941463721904,
      "uy": -0.23971597922008975,
      "base": 2.5959327678073847,
      "glowPhase": 8.034327874853167,
      "glowSpeed": 0.2261559642237947,
      "orbitPhase": 1.4028648346264645,
      "orbitSpeed": 0.0477922856864581,
      "orbitRadius": 7.51326740539793,
      "hub": false
    },
    {
      "ux": 0.09253339888348665,
      "uy": -0.6409900791500281,
      "base": 1.676640527161935,
      "glowPhase": 5.484285448303323,
      "glowSpeed": 0.22173632004176164,
      "orbitPhase": 6.000628260787981,
      "orbitSpeed": 0.05014384759267003,
      "orbitRadius": 7.970293260080044,
      "hub": false
    },
    {
      "ux": 0.1785798845967426,
      "uy": 0.5430305525102702,
      "base": 1.4844958996134336,
      "glowPhase": 8.094146712641182,
      "glowSpeed": 0.149947754886997,
      "orbitPhase": 0.7235984371800941,
      "orbitSpeed": 0.03722241808618041,
      "orbitRadius": 8.074377866074613,
      "hub": false
    },
    {
      "ux": 0.17653273923369084,
      "uy": -0.41006725302049324,
      "base": 2.281688018434015,
      "glowPhase": 8.544454581693191,
      "glowSpeed": 0.2577312826670845,
      "orbitPhase": 1.8309135020684228,
      "orbitSpeed": 0.06931557686824213,
      "orbitRadius": 9.47904236746883,
      "hub": false
    },
    {
      "ux": 0.05429441047787606,
      "uy": 0.647382159666655,
      "base": 3.117252695608642,
      "glowPhase": 6.906724536043442,
      "glowSpeed": 0.20722173727679832,
      "orbitPhase": 2.0909950106193262,
      "orbitSpeed": 0.0351605362896931,
      "orbitRadius": 8.282085310356662,
      "hub": true
    },
    {
      "ux": -0.06800940126130828,
      "uy": 0.7434058947849788,
      "base": 1.8484137609408655,
      "glowPhase": 8.88677507533862,
      "glowSpeed": 0.25759833379480124,
      "orbitPhase": 5.028643135518153,
      "orbitSpeed": 0.06430305362284894,
      "orbitRadius": 2.7080613625211196,
      "hub": false
    },
    {
      "ux": -0.6446335741873732,
      "uy": -0.3395307184611496,
      "base": 2.0046954153292678,
      "glowPhase": 3.0450352825056695,
      "glowSpeed": 0.1383158092075957,
      "orbitPhase": 5.8172164175365975,
      "orbitSpeed": 0.07819668095307253,
      "orbitRadius": 8.58899083081854,
      "hub": false
    }
  ],
  "edges": [
    {
      "a": 0,
      "b": 24,
      "w": 0.6397026646749646
    },
    {
      "a": 0,
      "b": 29,
      "w": 0.9407249330888648
    },
    {
      "a": 1,
      "b": 11,
      "w": 0.7784121909179276
    },
    {
      "a": 1,
      "b": 12,
      "w": 0.6405741130041065
    },
    {
      "a": 1,
      "b": 15,
      "w": 0.5040158421042186
    },
    {
      "a": 1,
      "b": 18,
      "w": 0.8611003912183702
    },
    {
      "a": 1,
      "b": 23,
      "w": 0.8024563616035758
    },
    {
      "a": 1,
      "b": 28,
      "w": 0.9950181114082974
    },
    {
      "a": 2,
      "b": 9,
      "w": 0.513155885100374
    },
    {
      "a": 2,
      "b": 15,
      "w": 0.5973329907597325
    },
    {
      "a": 2,
      "b": 21,
      "w": 0.5930780747568627
    },
    {
      "a": 3,
      "b": 25,
      "w": 0.9231282645378428
    },
    {
      "a": 3,
      "b": 26,
      "w": 0.8732904603789922
    },
    {
      "a": 3,
      "b": 28,
      "w": 0.4798422036414471
    },
    {
      "a": 4,
      "b": 5,
      "w": 0.546592614725592
    },
    {
      "a": 4,
      "b": 6,
      "w": 0.6109668539722145
    },
    {
      "a": 4,
      "b": 8,
      "w": 0.8736533372183646
    },
    {
      "a": 4,
      "b": 11,
      "w": 0.40234417153284113
    },
    {
      "a": 4,
      "b": 17,
      "w": 0.7539351630874719
    },
    {
      "a": 4,
      "b": 20,
      "w": 0.7227090007047812
    },
    {
      "a": 4,
      "b": 23,
      "w": 0.5680654084212939
    },
    {
      "a": 5,
      "b": 11,
      "w": 0.6555578806871125
    },
    {
      "a": 5,
      "b": 21,
      "w": 0.47586759707717013
    },
    {
      "a": 5,
      "b": 24,
      "w": 0.7540266905625379
    },
    {
      "a": 6,
      "b": 11,
      "w": 0.7242566589323445
    },
    {
      "a": 6,
      "b": 18,
      "w": 0.6448420315257815
    },
    {
      "a": 6,
      "b": 23,
      "w": 0.556587276372939
    },
    {
      "a": 6,
      "b": 27,
      "w": 0.5428873214420036
    },
    {
      "a": 7,
      "b": 14,
      "w": 0.6200446743241974
    },
    {
      "a": 7,
      "b": 17,
      "w": 0.9298276721837362
    },
    {
      "a": 7,
      "b": 22,
      "w": 0.5179066755272609
    },
    {
      "a": 7,
      "b": 24,
      "w": 0.7479470072325383
    },
    {
      "a": 7,
      "b": 25,
      "w": 0.8337785740990278
    },
    {
      "a": 8,
      "b": 19,
      "w": 0.908560401259729
    },
    {
      "a": 8,
      "b": 21,
      "w": 0.5760816440807215
    },
    {
      "a": 8,
      "b": 22,
      "w": 0.9309970396361844
    },
    {
      "a": 8,
      "b": 25,
      "w": 0.4789365785847468
    },
    {
      "a": 8,
      "b": 26,
      "w": 0.5223854839638467
    },
    {
      "a": 9,
      "b": 17,
      "w": 0.7115823854283905
    },
    {
      "a": 9,
      "b": 21,
      "w": 0.9624349645307754
    },
    {
      "a": 9,
      "b": 25,
      "w": 0.6062037139750776
    },
    {
      "a": 10,
      "b": 14,
      "w": 0.9758917143424061
    },
    {
      "a": 10,
      "b": 19,
      "w": 0.7277572432651453
    },
    {
      "a": 10,
      "b": 20,
      "w": 0.9811886637313065
    },
    {
      "a": 10,
      "b": 24,
      "w": 0.5710551782300591
    },
    {
      "a": 11,
      "b": 13,
      "w": 0.6861990420818695
    },
    {
      "a": 11,
      "b": 14,
      "w": 0.5300028207175747
    },
    {
      "a": 11,
      "b": 16,
      "w": 0.9291806675784118
    },
    {
      "a": 11,
      "b": 20,
      "w": 0.46170128949443906
    },
    {
      "a": 11,
      "b": 28,
      "w": 0.7835318152643731
    },
    {
      "a": 12,
      "b": 21,
      "w": 0.4037271256358811
    },
    {
      "a": 12,
      "b": 24,
      "w": 0.6045400447093445
    },
    {
      "a": 12,
      "b": 25,
      "w": 0.8383050654423323
    },
    {
      "a": 12,
      "b": 28,
      "w": 0.5281455585102226
    },
    {
      "a": 13,
      "b": 22,
      "w": 0.5230395689310952
    },
    {
      "a": 13,
      "b": 25,
      "w": 0.5256038437404701
    },
    {
      "a": 14,
      "b": 17,
      "w": 0.4684151658855871
    },
    {
      "a": 15,
      "b": 20,
      "w": 0.7441791837588154
    },
    {
      "a": 15,
      "b": 25,
      "w": 0.6642381155077894
    },
    {
      "a": 16,
      "b": 17,
      "w": 0.8223597122432436
    },
    {
      "a": 17,
      "b": 24,
      "w": 0.47889465330249165
    },
    {
      "a": 17,
      "b": 26,
      "w": 0.8295965657692164
    },
    {
      "a": 18,
      "b": 21,
      "w": 0.5795811077816978
    },
    {
      "a": 18,
      "b": 23,
      "w": 0.922879718869795
    },
    {
      "a": 18,
      "b": 27,
      "w": 0.52455019664827
    },
    {
      "a": 19,
      "b": 21,
      "w": 0.6893607770653172
    },
    {
      "a": 19,
      "b": 22,
      "w": 0.7651235324074369
    },
    {
      "a": 20,
      "b": 23,
      "w": 0.5790502365768173
    },
    {
      "a": 22,
      "b": 25,
      "w": 0.685355757163733
    },
    {
      "a": 25,
      "b": 28,
      "w": 0.9580388973070331
    },
    {
      "a": 28,
      "b": 29,
      "w": 0.9397829870622243
    }
  ]
};

  // Default theme config (locked to the exported model + matching render parameters)
  const DEFAULT_CONFIG = {
    // Used for deterministic pulses + grain. (Topology comes from LOCKED_MODEL.)
    seed: 539300390,

    // Fixed model (topology). Generation is skipped when this is present.
    model: LOCKED_MODEL,

    scale: 1.14,
    fps: 30,
    idleOpacity: 0.8,
    activeOpacity: 0.35,
    nodeCount: 30,
    connectionProb: 0.18,
    curveStrength: 1,
    nodeBaseSize: 2,
    hubChance: 0.1,
    hubMul: 2,
    pulsesPerSpawn: 2,
    spawnInterval: 900,
    pulseSpeed: 0.5,
    pulseBrightness: 0.34,
    bg0: "#000000",
    bg1: "#04162a",
    edgeCol: "#01599d",
    edgeAlpha: 0.12,
    pulseCol: "#71bcfe",
    nodeGlow: 0.55
  };


  // -----------------------------
  // State
  // -----------------------------
  let enabled = false;
  let cfg = { ...DEFAULT_CONFIG };

  let mode = MODE_ACTIVE;
  let idleTimerId = null;

  let overlayEl = null;
  let canvasEl = null;
  let ctx = null;
  let rootClassObserver = null;
  let overlayObserver = null;

  let rafId = null;

  // Canvas sizing (CSS pixels)
  let W = 0;
  let H = 0;
  let dpr = 1;

  // Animation timing
  let lastTime = performance.now();
  let lastDraw = 0;
  let lastPulseSpawn = performance.now();

  // Topology (deterministic)
  // nodes use normalized ellipse coords (ux/uy) so resizes just re-layout.
  let nodes = [];
  let edges = [];
  let pulses = [];

  // PRNGs (seeded)
  let topoRng = null;
  let pulseRng = null;

  // Noise pattern cache
  let noiseCanvas = null;
  let noisePattern = null;

  // Activity listeners (so we can detach cleanly)
  let activityHandler = null;

  function log() {
    try { console.log("[VAL " + MODULE_NAME + "]", ...arguments); } catch (_) {}
  }

  // -----------------------------
  // Storage helpers
  // -----------------------------
  function readBoolLS(key, fallback) {
    try {
      const v = localStorage.getItem(key);
      if (v === "1" || v === "true") return true;
      if (v === "0" || v === "false") return false;
    } catch (_) {}
    return !!fallback;
  }

  function writeBoolLS(key, value) {
    try { localStorage.setItem(key, value ? "1" : "0"); } catch (_) {}
  }

  function loadConfigFromStorage() {
    try {
      const raw = localStorage.getItem(LS_THEME_CONFIG);
      if (!raw) return;
      const parsed = JSON.parse(raw);
      cfg = normalizeIncomingConfig(parsed);
    } catch (_) {
      cfg = { ...DEFAULT_CONFIG };
    }
  }

  function saveConfigToStorage() {
    try { localStorage.setItem(LS_THEME_CONFIG, JSON.stringify(cfg)); } catch (_) {}
  }

  function normalizeIncomingConfig(input) {
    // Supports two shapes:
    //  1) Flat config: { seed, scale, nodeCount, ... , model? }
    //  2) Tuner export bundle: { cfg:{...}, model:{...} } (plus optional metadata)
    try {
      if (!input || typeof input !== "object") return { ...DEFAULT_CONFIG };

      // Bundle form
      if (input.cfg && typeof input.cfg === "object") {
        const flat = { ...input.cfg };
        // Model can live at the top-level or inside cfg
        if (input.model && typeof input.model === "object") flat.model = input.model;
        if (input.cfg.model && typeof input.cfg.model === "object") flat.model = input.cfg.model;
        return { ...DEFAULT_CONFIG, ...flat };
      }

      // Flat form
      return { ...DEFAULT_CONFIG, ...input };
    } catch (_) {
      return { ...DEFAULT_CONFIG };
    }
  }


  // -----------------------------
  // Deterministic PRNG
  // -----------------------------
  function mulberry32(seed) {
    let t = (seed >>> 0) || 0;
    return function () {
      t += 0x6D2B79F5;
      let x = t;
      x = Math.imul(x ^ (x >>> 15), x | 1);
      x ^= x + Math.imul(x ^ (x >>> 7), x | 61);
      return ((x ^ (x >>> 14)) >>> 0) / 4294967296;
    };
  }

  function rand(rng, min, max) {
    return rng() * (max - min) + min;
  }

  function clamp(v, a, b) {
    return Math.max(a, Math.min(b, v));
  }

  // -----------------------------
  // DOM + layering
  // -----------------------------
  function applyRootClass(on) {
    try {
      const root = document.documentElement;
      if (!root) return;
      root.classList.toggle("val-theme-enabled", !!on);
    } catch (_) {}
  }

  function applyOverlayInlineStyles() {
    if (!overlayEl) return;
    overlayEl.style.position = "fixed";
    overlayEl.style.inset = "0";
    overlayEl.style.pointerEvents = "none";
    overlayEl.style.zIndex = "9997";
    overlayEl.style.transition = "opacity 1s ease";
    overlayEl.style.willChange = "opacity";

    if (canvasEl) {
      canvasEl.style.width = "100%";
      canvasEl.style.height = "100%";
      canvasEl.style.display = "block";
    }
  }

  function ensureOverlay() {
    if (overlayEl && canvasEl && ctx) {
      // If the overlay was removed from the DOM (e.g., SPA transition), treat refs as stale.
      if (overlayEl.isConnected && canvasEl.isConnected) {
        applyOverlayInlineStyles();
        return;
      }
      overlayEl = null;
      canvasEl = null;
      ctx = null;
    }

    // Remove any stale node from previous versions
    try {
      const old = document.getElementById("val-theme-overlay");
      if (old && old.parentElement) old.parentElement.removeChild(old);
    } catch (_) {}

    const overlay = document.createElement("div");
    overlay.id = "val-theme-overlay";
    overlay.className = "val-theme-idle-overlay";
    overlay.style.opacity = String(cfg.activeOpacity);

    const canvas = document.createElement("canvas");
    canvas.className = "val-theme-idle-overlay-canvas";
    overlay.appendChild(canvas);

    const host = document.body || document.documentElement;
    if (!host) return;
    host.appendChild(overlay);

    overlayEl = overlay;
    canvasEl = canvas;
    applyOverlayInlineStyles();
    ctx = canvas.getContext("2d", { alpha: true, desynchronized: true });

    resizeCanvas();
    window.addEventListener("resize", resizeCanvas, { passive: true });

    log("Overlay created");
  }

  function ensureOverlayPresence() {
    if (!enabled) return;
    if (!overlayEl || !canvasEl || !ctx || !overlayEl.isConnected || !canvasEl.isConnected) {
      try { ensureOverlay(); } catch (_) {}
      return;
    }
    applyOverlayInlineStyles();
  }

  function removeOverlay() {
    try { window.removeEventListener("resize", resizeCanvas, { passive: true }); } catch (_) {}
    try {
      if (overlayEl && overlayEl.parentElement) overlayEl.parentElement.removeChild(overlayEl);
    } catch (_) {}
    overlayEl = null;
    canvasEl = null;
    ctx = null;
    noiseCanvas = null;
    noisePattern = null;
  }

  function resizeCanvas() {
    if (!canvasEl || !ctx) return;

    W = window.innerWidth || 0;
    H = window.innerHeight || 0;

    dpr = Math.min(2, window.devicePixelRatio || 1);

    canvasEl.width = Math.max(1, Math.floor(W * dpr));
    canvasEl.height = Math.max(1, Math.floor(H * dpr));

    // Render in CSS pixels
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

    // Re-layout, do NOT re-roll topology
    layoutNodes();
    buildNoisePattern();

    // Redraw immediately so resize feels responsive even under FPS cap.
    lastDraw = 0;
  }

  // -----------------------------
  // Topology (deterministic + stable)
  // -----------------------------
  function hasFixedModel() {
    try {
      const m = cfg && cfg.model;
      return !!(m && typeof m === "object" && Array.isArray(m.nodes) && Array.isArray(m.edges) && m.nodes.length > 0);
    } catch (_) {
      return false;
    }
  }

  function loadFixedModel(model) {
    nodes = [];
    edges = [];
    pulses = [];

    const nodeList = (model && Array.isArray(model.nodes)) ? model.nodes : [];
    const edgeList = (model && Array.isArray(model.edges)) ? model.edges : [];

    for (const raw of nodeList) {
      if (!raw || typeof raw !== "object") continue;

      const ux = clamp(parseFloat(raw.ux), -1.25, 1.25);
      const uy = clamp(parseFloat(raw.uy), -1.25, 1.25);
      if (!Number.isFinite(ux) || !Number.isFinite(uy)) continue;

      const base = clamp(parseFloat(raw.base ?? raw.baseSize ?? cfg.nodeBaseSize), 0.2, 30);

      nodes.push({
        ux, uy,
        x: 0, y: 0, fx: 0, fy: 0,
        base,

        glowPhase: parseFloat(raw.glowPhase ?? 0) || 0,
        glowSpeed: clamp(parseFloat(raw.glowSpeed ?? 0.2), 0, 3),
        glow: 0.2,

        orbitPhase: parseFloat(raw.orbitPhase ?? 0) || 0,
        orbitSpeed: clamp(parseFloat(raw.orbitSpeed ?? 0.05), 0, 3),
        orbitRadius: clamp(parseFloat(raw.orbitRadius ?? 0), 0, 80),

        hub: !!raw.hub
      });
    }

    const n = nodes.length;

    for (const raw of edgeList) {
      if (!raw || typeof raw !== "object") continue;

      const a = raw.a | 0;
      const b = raw.b | 0;
      if (a < 0 || b < 0 || a >= n || b >= n || a === b) continue;

      const w = clamp(parseFloat(raw.w ?? raw.weight ?? 1), 0.1, 3);
      edges.push({ a, b, w });
    }

    // Keep a consistent layout policy across any viewport size.
    layoutNodes();

    lastTime = performance.now();
    lastPulseSpawn = performance.now();
  }

  function buildTopology() {
    // Reset seeded RNGs
    topoRng = mulberry32((cfg.seed || 0) >>> 0);
    pulseRng = mulberry32(((cfg.seed || 0) + 1) >>> 0);

    // If a fixed model is present, skip procedural generation entirely.
    if (hasFixedModel()) {
      loadFixedModel(cfg.model);
      return;
    }

    nodes = [];
    edges = [];
    pulses = [];

    const count = Math.max(0, (cfg.nodeCount | 0));

    for (let i = 0; i < count; i++) {
      const a = rand(topoRng, 0, Math.PI * 2);
      const r = Math.sqrt(topoRng()); // uniform disk density
      const ux = Math.cos(a) * r;
      const uy = Math.sin(a) * r * 0.75;

      const isHub = topoRng() < clamp(cfg.hubChance, 0, 1);
      const base = cfg.nodeBaseSize * rand(topoRng, 0.65, 1.35) * (isHub ? cfg.hubMul : 1);

      nodes.push({
        ux, uy,
        x: 0, y: 0, fx: 0, fy: 0,
        base,

        glowPhase: rand(topoRng, 0, Math.PI * 2),
        glowSpeed: rand(topoRng, 0.12, 0.32),
        glow: 0.2,

        orbitPhase: rand(topoRng, 0, Math.PI * 2),
        orbitSpeed: rand(topoRng, 0.03, 0.09),
        orbitRadius: rand(topoRng, 2, 10),

        hub: isHub
      });
    }

    const p = clamp(cfg.connectionProb, 0, 1);

    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        if (topoRng() < p) {
          edges.push({ a: i, b: j, w: rand(topoRng, 0.4, 1.0) });
        }
      }
    }

    layoutNodes();

    lastTime = performance.now();
    lastPulseSpawn = performance.now();
  }

  function layoutNodes() {
    if (!nodes || !nodes.length) return;

    const pad = Math.min(W, H) * 0.16;
    const cx = W / 2;
    const cy = H / 2;
    const rx = Math.max(1, (W - pad * 2) / 2);
    const ry = Math.max(1, (H - pad * 2) / 2);

    for (const n of nodes) {
      n.x = cx + n.ux * rx;
      n.y = cy + n.uy * ry;
      n.fx = n.x;
      n.fy = n.y;
    }
  }

  // -----------------------------
  // Noise (static)
  // -----------------------------
  function buildNoisePattern() {
    try {
      if (!ctx) return;

      // Build once per resize; small tile is enough.
      const tile = 192;

      noiseCanvas = document.createElement("canvas");
      noiseCanvas.width = tile;
      noiseCanvas.height = tile;

      const nctx = noiseCanvas.getContext("2d");
      if (!nctx) return;

      const img = nctx.createImageData(tile, tile);
      const data = img.data;

      // Deterministic grain uses topoRng (same seed) but does not affect topology
      const grainRng = mulberry32(((cfg.seed || 0) + 9999) >>> 0);

      for (let i = 0; i < data.length; i += 4) {
        const v = Math.floor(grainRng() * 255);
        data[i] = v;
        data[i + 1] = v;
        data[i + 2] = v;
        data[i + 3] = 22; // alpha
      }

      nctx.putImageData(img, 0, 0);
      noisePattern = ctx.createPattern(noiseCanvas, "repeat");
    } catch (_) {
      noiseCanvas = null;
      noisePattern = null;
    }
  }

  // -----------------------------
  // Colors helpers
  // -----------------------------
  function hexToRgb(hex) {
    try {
      const h = String(hex || "").replace("#", "").trim();
      if (h.length !== 6) return { r: 0, g: 0, b: 0 };
      return {
        r: parseInt(h.slice(0, 2), 16) || 0,
        g: parseInt(h.slice(2, 4), 16) || 0,
        b: parseInt(h.slice(4, 6), 16) || 0
      };
    } catch (_) {
      return { r: 0, g: 0, b: 0 };
    }
  }

  function hexToRgba(hex, a) {
    const { r, g, b } = hexToRgb(hex);
    return `rgba(${r},${g},${b},${a})`;
  }

  // -----------------------------
  // Animation
  // -----------------------------
  function setMode(next) {
    mode = next === MODE_IDLE ? MODE_IDLE : MODE_ACTIVE;
    try {
      if (overlayEl) {
        overlayEl.style.opacity = String(mode === MODE_IDLE ? cfg.idleOpacity : cfg.activeOpacity);
      }
    } catch (_) {}
  }

  function handleActivity() {
    setMode(MODE_ACTIVE);

    if (idleTimerId) {
      try { clearTimeout(idleTimerId); } catch (_) {}
    }

    idleTimerId = setTimeout(() => setMode(MODE_IDLE), IDLE_TIMEOUT_MS);
  }

  function attachActivityListeners() {
    if (activityHandler) return;

    activityHandler = () => handleActivity();

    window.addEventListener("keydown", activityHandler, true);
    window.addEventListener("mousedown", activityHandler, true);
    window.addEventListener("pointerdown", activityHandler, true);
    window.addEventListener("wheel", activityHandler, { passive: true });
    window.addEventListener("touchstart", activityHandler, { passive: true });

    // Start active, arm idle timer
    setMode(MODE_ACTIVE);
    handleActivity();
  }

  function detachActivityListeners() {
    if (!activityHandler) return;

    try { window.removeEventListener("keydown", activityHandler, true); } catch (_) {}
    try { window.removeEventListener("mousedown", activityHandler, true); } catch (_) {}
    try { window.removeEventListener("pointerdown", activityHandler, true); } catch (_) {}
    try { window.removeEventListener("wheel", activityHandler, { passive: true }); } catch (_) {}
    try { window.removeEventListener("touchstart", activityHandler, { passive: true }); } catch (_) {}

    activityHandler = null;
  }

  function spawnPulse() {
    if (!edges.length) return;

    const idx = Math.floor(pulseRng() * edges.length);
    const forward = pulseRng() > 0.5;

    pulses.push({
      edgeIndex: idx,
      t: forward ? 0 : 1,
      dir: forward ? 1 : -1,
      life: 1
    });
  }

  function update(dtMs) {
    const dt = dtMs / 1000;

    // Update nodes (glow + gentle orbit)
    for (const n of nodes) {
      n.glowPhase += n.glowSpeed * dt;
      const s = 0.5 + 0.5 * Math.sin(n.glowPhase);
      n.glow = 0.2 + clamp(cfg.nodeGlow, 0, 1) * s;

      n.orbitPhase += n.orbitSpeed * dt;
      const ox = Math.cos(n.orbitPhase) * n.orbitRadius;
      const oy = Math.sin(n.orbitPhase) * n.orbitRadius * 0.6;
      n.fx = n.x + ox;
      n.fy = n.y + oy;
    }

    const now = performance.now();
    const spawnEvery = Math.max(40, cfg.spawnInterval | 0);

    if (now - lastPulseSpawn > spawnEvery) {
      const n = Math.max(0, cfg.pulsesPerSpawn | 0);
      for (let i = 0; i < n; i++) spawnPulse();
      lastPulseSpawn = now;
    }

    // Advance pulses
    pulses = pulses.filter(p => p.life > 0);

    for (const p of pulses) {
      p.t += cfg.pulseSpeed * dt * p.dir;
      p.life -= dt * 0.30;
    }

    pulses = pulses.filter(p => p.t >= 0 && p.t <= 1 && p.life > 0);
  }

  function draw() {
    if (!ctx) return;

    // Clear
    ctx.clearRect(0, 0, W, H);

    // Background
    ctx.fillStyle = cfg.bg0;
    ctx.fillRect(0, 0, W, H);

    const maxR = Math.max(W, H) * 0.85;
    const g = ctx.createRadialGradient(W * 0.5, H * 0.5, maxR * 0.05, W * 0.5, H * 0.5, maxR);
    g.addColorStop(0, hexToRgba(cfg.bg1, 0.85));
    g.addColorStop(0.5, hexToRgba(cfg.bg1, 0.28));
    g.addColorStop(1, "rgba(0,0,0,0)");
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, W, H);

    // Grain (static)
    if (noisePattern) {
      ctx.save();
      ctx.globalAlpha = 0.05;
      ctx.fillStyle = noisePattern;
      ctx.fillRect(0, 0, W, H);
      ctx.restore();
    }

    // Apply overall scale about center
    const s = Math.max(0.25, cfg.scale || 1);
    ctx.save();
    ctx.translate(W / 2, H / 2);
    ctx.scale(s, s);
    ctx.translate(-W / 2, -H / 2);

    // Edges
    const edgeRgb = hexToRgb(cfg.edgeCol);
    ctx.lineCap = "round";

    for (const e of edges) {
      const a = nodes[e.a];
      const b = nodes[e.b];
      if (!a || !b) continue;

      const ax = a.fx, ay = a.fy, bx = b.fx, by = b.fy;

      const cxm = (ax + bx) / 2;
      const cym = (ay + by) / 2;
      const dx = bx - ax;
      const dy = by - ay;
      const len = Math.hypot(dx, dy) || 1;
      const nx = -dy / len;
      const ny = dx / len;

      const curvature = clamp(cfg.curveStrength, 0, 2) * Math.min(60, len * 0.2);
      const ctrlX = cxm + nx * curvature;
      const ctrlY = cym + ny * curvature;

      const aAlpha = clamp(cfg.edgeAlpha, 0, 1) * e.w;
      ctx.strokeStyle = `rgba(${edgeRgb.r},${edgeRgb.g},${edgeRgb.b},${aAlpha})`;
      ctx.lineWidth = 0.6 + e.w * 0.9;

      ctx.beginPath();
      ctx.moveTo(ax, ay);
      ctx.quadraticCurveTo(ctrlX, ctrlY, bx, by);
      ctx.stroke();
    }

    // Pulses
    const pulseRgb = hexToRgb(cfg.pulseCol);

    for (const p of pulses) {
      const e = edges[p.edgeIndex];
      if (!e) continue;

      const a = nodes[e.a];
      const b = nodes[e.b];
      if (!a || !b) continue;

      const ax = a.fx, ay = a.fy, bx = b.fx, by = b.fy;

      const cxm = (ax + bx) / 2;
      const cym = (ay + by) / 2;
      const dx = bx - ax;
      const dy = by - ay;
      const len = Math.hypot(dx, dy) || 1;
      const nx = -dy / len;
      const ny = dx / len;

      const curvature = clamp(cfg.curveStrength, 0, 2) * Math.min(60, len * 0.2);
      const ctrlX = cxm + nx * curvature;
      const ctrlY = cym + ny * curvature;

      const t = p.t;
      const x = (1 - t) * (1 - t) * ax + 2 * (1 - t) * t * ctrlX + t * t * bx;
      const y = (1 - t) * (1 - t) * ay + 2 * (1 - t) * t * ctrlY + t * t * by;

      const pulseSize = 2.2 + 2.4 * e.w;
      const pulseAlpha = clamp(cfg.pulseBrightness, 0, 1) * (0.25 + 0.75 * p.life);

      const grad = ctx.createRadialGradient(x, y, 0, x, y, pulseSize * 3.2);
      grad.addColorStop(0, `rgba(255,255,255,${pulseAlpha})`);
      grad.addColorStop(0.28, `rgba(${pulseRgb.r},${pulseRgb.g},${pulseRgb.b},${pulseAlpha})`);
      grad.addColorStop(1, "rgba(0,0,0,0)");

      ctx.fillStyle = grad;
      ctx.beginPath();
      ctx.arc(x, y, pulseSize * 3.2, 0, Math.PI * 2);
      ctx.fill();
    }

    // Nodes
    for (const n of nodes) {
      const base = n.base;
      const glowScale = 0.6 + n.glow * 0.8;
      const r = base * glowScale;

      const x = n.fx, y = n.fy;

      const alphaCore = 0.55 * n.glow;
      const alphaHalo = 0.18 * n.glow;

      const grad = ctx.createRadialGradient(x, y, 0, x, y, r * 3);
      grad.addColorStop(0, `rgba(255,255,255,${alphaCore})`);
      grad.addColorStop(0.32, `rgba(${pulseRgb.r},${pulseRgb.g},${pulseRgb.b},${alphaCore})`);
      grad.addColorStop(1, `rgba(0,0,0,${alphaHalo})`);

      ctx.fillStyle = grad;
      ctx.beginPath();
      ctx.arc(x, y, r * 2.6, 0, Math.PI * 2);
      ctx.fill();
    }

    ctx.restore(); // scale
  }

  function loop(now) {
    if (!enabled) return;

    // Self-heal if ChatGPT's SPA navigation removed the overlay node.
    ensureOverlayPresence();

    const dt = now - lastTime;
    lastTime = now;

    update(dt);

    const frameBudget = 1000 / Math.max(10, (cfg.fps | 0) || 30);
    if (now - lastDraw >= frameBudget) {
      draw();
      lastDraw = now;
    }

    rafId = requestAnimationFrame(loop);
  }

  // -----------------------------
  // Enable / Disable
  // -----------------------------
  function enableTheme() {
    if (enabled) {
      // Idempotent: if a SPA transition removed injected nodes, re-assert them.
      applyRootClass(true);
      try { ensureOverlay(); } catch (_) {}

      if (!nodes.length || !edges.length) {
        buildTopology();
      } else {
        layoutNodes();
      }

      attachActivityListeners();

      lastTime = performance.now();
      lastDraw = 0;
      if (!rafId) {
        rafId = requestAnimationFrame(loop);
      }
      return;
    }

    enabled = true;
    writeBoolLS(LS_THEME_ENABLED, true);

    applyRootClass(true);
    ensureOverlay();

    // Build deterministic topology once per enable/config change
    if (!nodes.length || !edges.length) {
      buildTopology();
    } else {
      layoutNodes();
    }

    attachActivityListeners();

    lastTime = performance.now();
    lastDraw = 0;

    if (!rafId) {
      rafId = requestAnimationFrame(loop);
    }

    log("Enabled");
  }

  function disableTheme() {
    if (!enabled) return;

    enabled = false;
    writeBoolLS(LS_THEME_ENABLED, false);

    try {
      if (rafId) cancelAnimationFrame(rafId);
    } catch (_) {}
    rafId = null;

    try {
      if (idleTimerId) clearTimeout(idleTimerId);
    } catch (_) {}
    idleTimerId = null;

    detachActivityListeners();
    removeOverlay();
    applyRootClass(false);

    log("Disabled");
  }

  function setEnabled(next) {
    const v = !!next;
    if (v) enableTheme();
    else disableTheme();
  }

  // -----------------------------
  // Config updates
  // -----------------------------
  function setConfig(partial, persist) {
    if (!partial || typeof partial !== "object") return;

    // Accept "bundle" shape produced by the tuner:
    //   { cfg:{...}, model:{...} } (plus optional metadata)
    // and normalize it into a flat config object.
    if (partial.cfg && typeof partial.cfg === "object") {
      const model = (partial.model && typeof partial.model === "object") ? partial.model
                  : (partial.cfg.model && typeof partial.cfg.model === "object") ? partial.cfg.model
                  : null;
      partial = { ...partial.cfg, model };
    }

    const before = { ...cfg };
    cfg = { ...cfg, ...partial };

    // Normalize a few values to keep it sane
    cfg.fps = clamp(cfg.fps | 0, 10, 60);
    cfg.scale = clamp(parseFloat(cfg.scale || 1), 0.25, 3.0);

    cfg.idleOpacity = clamp(parseFloat(cfg.idleOpacity), 0, 1);
    cfg.activeOpacity = clamp(parseFloat(cfg.activeOpacity), 0, 1);

    cfg.nodeCount = clamp(cfg.nodeCount | 0, 0, 500);
    cfg.connectionProb = clamp(parseFloat(cfg.connectionProb), 0, 1);
    cfg.curveStrength = clamp(parseFloat(cfg.curveStrength), 0, 2);

    cfg.nodeBaseSize = clamp(parseFloat(cfg.nodeBaseSize), 0.2, 12);
    cfg.hubChance = clamp(parseFloat(cfg.hubChance), 0, 1);
    cfg.hubMul = clamp(parseFloat(cfg.hubMul), 1, 8);

    cfg.pulsesPerSpawn = clamp(cfg.pulsesPerSpawn | 0, 0, 10);
    cfg.spawnInterval = clamp(cfg.spawnInterval | 0, 40, 60000);
    cfg.pulseSpeed = clamp(parseFloat(cfg.pulseSpeed), 0.01, 3);
    cfg.pulseBrightness = clamp(parseFloat(cfg.pulseBrightness), 0, 1);

    cfg.edgeAlpha = clamp(parseFloat(cfg.edgeAlpha), 0, 1);
    cfg.nodeGlow = clamp(parseFloat(cfg.nodeGlow), 0, 1);

    cfg.seed = (cfg.seed >>> 0);

    if (persist !== false) saveConfigToStorage();

    // Topology-affecting settings?
    const topoKeys = ["seed", "nodeCount", "connectionProb", "hubChance", "hubMul", "nodeBaseSize", "model"];
    const topoChanged = topoKeys.some(k => before[k] !== cfg[k]) || ("model" in partial);

    if (topoChanged) {
      buildTopology();
    } else {
      // Layout could still change if scale etc. changed.
      layoutNodes();
    }

    // Immediately apply opacity for current mode
    setMode(mode);

    // Ensure overlay exists if enabled (so config changes can be applied live)
    if (enabled) {
      ensureOverlay();
      resizeCanvas();
    }
  }

  // -----------------------------
  // External API (Dock calls this)
  // -----------------------------
  function exposeApi() {
    try {
      window.VAL_Theme = window.VAL_Theme || {};
      window.VAL_Theme.isEnabled = () => enabled;
      window.VAL_Theme.setEnabled = (v) => setEnabled(!!v);
      window.VAL_Theme.toggle = () => setEnabled(!enabled);

      window.VAL_Theme.getConfig = () => ({ ...cfg });
      window.VAL_Theme.setConfig = (partial, persist) => setConfig(partial, persist);
      window.VAL_Theme.regenerate = (seed) => {
        if (seed != null) cfg.seed = (seed >>> 0);
        buildTopology();
      };
    } catch (_) {}
  }

  // -----------------------------
  // Message bridge (fallback for Dock)
  // -----------------------------
  function attachMessageBridge() {
    try {
      window.addEventListener("message", (ev) => {
        const msg = ev && ev.data;
        if (!msg || typeof msg !== "object") return;

        if (msg.type === "val.theme.set_enabled") {
          setEnabled(!!msg.enabled);
        }

        if (msg.type === "val.theme.set_config" && msg.config) {
          setConfig(msg.config, msg.persist !== false);
        }

        if (msg.type === "val.theme.regenerate") {
          const seed = (msg.seed == null) ? null : (msg.seed >>> 0);
          if (seed != null) cfg.seed = seed;
          buildTopology();
        }
      }, true);
    } catch (_) {}
  }

  // -----------------------------
  // SPA resilience
  // -----------------------------
  function attachResilienceObservers() {
    try {
      if (rootClassObserver) rootClassObserver.disconnect();
      rootClassObserver = new MutationObserver((mutations) => {
        if (!enabled) return;
        for (const mutation of mutations) {
          if (mutation.type !== "attributes" || mutation.attributeName !== "class") continue;
          const root = document.documentElement;
          if (!root) return;
          if (!root.classList.contains("val-theme-enabled")) {
            root.classList.add("val-theme-enabled");
          }
          ensureOverlayPresence();
          break;
        }
      });
      const root = document.documentElement;
      if (root) {
        rootClassObserver.observe(root, { attributes: true, attributeFilter: ["class"] });
      }
    } catch (_) {}

    try {
      if (overlayObserver) overlayObserver.disconnect();
      overlayObserver = new MutationObserver(() => {
        if (!enabled) return;
        if (!document.getElementById("val-theme-overlay")) {
          ensureOverlayPresence();
          return;
        }
        ensureOverlayPresence();
      });
      const target = document.documentElement;
      if (target) {
        overlayObserver.observe(target, { childList: true, subtree: true });
      }
    } catch (_) {}
  }

  // -----------------------------
  // Boot
  // -----------------------------
  function boot() {
    loadConfigFromStorage();
    exposeApi();
    attachMessageBridge();
    attachResilienceObservers();

    // Defensive cleanup in case the script was hot-reloaded or injected twice.
    // We want boot to be idempotent and not require a manual toggle.
    try {
      document.documentElement && document.documentElement.classList.remove("val-theme-enabled");
    } catch (_) {}
    try {
      const stale = document.getElementById("val-theme-overlay");
      if (stale && stale.parentElement) stale.parentElement.removeChild(stale);
    } catch (_) {}

    // Source of truth is localStorage (Dock writes this). IMPORTANT:
// Do NOT assign to `enabled` before calling enableTheme(), or enableTheme()
// will early-return and the theme won't initialize until the user toggles.
//
// First-run default: if the enabled key does not exist yet, default the theme ON once.
const rawEnabled = (() => { try { return localStorage.getItem(LS_THEME_ENABLED); } catch (_) { return null; } })();
const wantEnabled = (rawEnabled === null) ? true : readBoolLS(LS_THEME_ENABLED, false);
enabled = false;
setEnabled(wantEnabled);

    log("booted (deterministic + toggleable)");
  }

  if (document.readyState === "complete" || document.readyState === "interactive") {
    boot();
  } else {
    window.addEventListener("DOMContentLoaded", boot, { once: true });
  }
})();
