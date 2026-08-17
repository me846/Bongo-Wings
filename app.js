(function () {
  "use strict";

  var CANVAS_SIZE = 1514;
  var ASSET_VERSION = "?v=characters-1";
  var DEFAULT_CHARACTER = "angelis";
  var CHROMA_KEY_TOLERANCE = 8;
  var TRANSPARENT_PIXEL = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/%3E";
  var LEVER_PIVOT = { x: 868.5, y: 1316 };
  var STICK_LENGTH = 89;
  var MAX_TILT_RADIANS = 35 * Math.PI / 180;
  var PANEL_RIGHT_ANGLE = 8.2 * Math.PI / 180;
  var PANEL_FORWARD_ANGLE = 26 * Math.PI / 180;
  var PANEL_RIGHT_AXIS = {
    x: Math.cos(PANEL_RIGHT_ANGLE),
    y: Math.sin(PANEL_RIGHT_ANGLE)
  };
  var PANEL_FORWARD_AXIS = {
    x: -Math.sin(PANEL_FORWARD_ANGLE),
    y: Math.cos(PANEL_FORWARD_ANGLE)
  };
  var DEPTH_PROJECTION = 0.5;
  var DEPTH_SCALE = 0.16;
  var LEVER_SOURCE_PIVOT = { x: 95.5, y: 240 };
  var LEVER_SOURCE_CONNECTOR = { x: 96, y: 150 };
  var FRONT_HOMOGRAPHY = [
    0.794159567, 0.313671696, 2.84459019,
    -0.208087556, 1.13801231, 41.1116954,
    -0.00140313632, 0.00137943165, 1
  ];
  var FRONT_ONLY_THRESHOLD = -0.85;
  var BACK_HOMOGRAPHY = [
    0.505713177, -0.231288326, 60.2554247,
    -0.148761386, 0.508626958, 28.8186338,
    -0.00139529189, -0.00122296994, 1
  ];
  var BACK_ONLY_THRESHOLD = 0.85;
  var AXIS_DEAD_ZONE = 0.18;
  var SHAFT_CROP = { x: 74, y: 150, width: 44, height: 90 };
  var GRIP_CROP = { x: 50, y: 55, width: 112, height: 97 };
  var GRIP_ANCHOR = { x: 46, y: 95 };

  var GAMEPAD_DEFAULTS = {
    "direction-up": ["Button12", "Axis1-"],
    "direction-down": ["Button13", "Axis1+"],
    "direction-left": ["Button14", "Axis0-"],
    "direction-right": ["Button15", "Axis0+"],
    "button-4": ["Button2"],
    "button-5": ["Button3"],
    "button-6": ["Button5"],
    "button-7": ["Button4"],
    "button-0": ["Button0"],
    "button-1": ["Button1"],
    "button-2": ["Button7"],
    "button-3": ["Button6"]
  };
  var BUTTON_CENTRES = [
    { x: 467.77, y: 1287.21 },
    { x: 537.68, y: 1294.82 },
    { x: 608.84, y: 1299.98 },
    { x: 682.1, y: 1306.41 },
    { x: 497.23, y: 1237.78 },
    { x: 570.41, y: 1247.47 },
    { x: 640.17, y: 1252.77 },
    { x: 709.27, y: 1254.06 }
  ];
  var HAND_ALPHA_CENTRE = { x: 61.4595, y: 59.0382 };

  var KEY_ACTIONS = [
    {
      id: "direction-up",
      group: "レバー",
      label: "上",
      kind: "direction",
      value: "up",
      defaults: ["ArrowUp", "KeyW"]
    },
    {
      id: "direction-down",
      group: "レバー",
      label: "下",
      kind: "direction",
      value: "down",
      defaults: ["ArrowDown", "KeyS"]
    },
    {
      id: "direction-left",
      group: "レバー",
      label: "左",
      kind: "direction",
      value: "left",
      defaults: ["ArrowLeft", "KeyA"]
    },
    {
      id: "direction-right",
      group: "レバー",
      label: "右",
      kind: "direction",
      value: "right",
      defaults: ["ArrowRight", "KeyD"]
    },
    { id: "button-4", group: "上段ボタン", label: "上段 1", kind: "button", value: 4, defaults: ["KeyU"] },
    { id: "button-5", group: "上段ボタン", label: "上段 2", kind: "button", value: 5, defaults: ["KeyI"] },
    { id: "button-6", group: "上段ボタン", label: "上段 3", kind: "button", value: 6, defaults: ["KeyO"] },
    { id: "button-7", group: "上段ボタン", label: "上段 4", kind: "button", value: 7, defaults: ["KeyP"] },
    { id: "button-0", group: "下段ボタン", label: "下段 1", kind: "button", value: 0, defaults: ["KeyJ"] },
    { id: "button-1", group: "下段ボタン", label: "下段 2", kind: "button", value: 1, defaults: ["KeyK"] },
    { id: "button-2", group: "下段ボタン", label: "下段 3", kind: "button", value: 2, defaults: ["KeyL"] },
    { id: "button-3", group: "下段ボタン", label: "下段 4", kind: "button", value: 3, defaults: ["Semicolon"] }
  ];

  var artwork = document.getElementById("artwork");
  var backgroundImage = document.getElementById("backgroundImage");
  var leverCanvas = document.getElementById("leverCanvas");
  var context = leverCanvas.getContext("2d", { alpha: true });
  var actionHand = document.getElementById("actionHand");
  var debugPanel = document.getElementById("debugPanel");
  var connectionStatus = document.getElementById("connectionStatus");
  var inputState = document.getElementById("inputState");
  var liveStatus = document.getElementById("liveStatus");
  var inputRelayStatus = document.getElementById("inputRelayStatus");
  var params = new URLSearchParams(window.location.search);

  var leverTexture = null;
  var leverTextureReady = false;
  var characterAssets = {};
  var currentCharacter = DEFAULT_CHARACTER;
  var pressedKeyCodes = new Set();
  var relayPressedKeyCodes = new Set();
  var relayGamepad = null;
  var keyBindings = defaultKeyBindings();
  var gamepadBindings = defaultGamepadBindings();
  var previousButtons = new Set();
  var activationOrder = new Map();
  var activationSequence = 0;
  var lastRenderedButton = -1;
  var lastBackgroundState = 1;
  var announcedGamepad = "";
  var currentVector = { x: 0, y: 0 };

  if (params.get("debug") === "1") {
    debugPanel.hidden = false;
  }

  artwork.dataset.fit = params.get("fit") === "fill" ? "fill" : "contain";
  currentCharacter = normalizeCharacter(params.get("character"));

  startInputRelay();

  context.imageSmoothingEnabled = true;
  context.imageSmoothingQuality = "high";

  backgroundImage.src = TRANSPARENT_PIXEL;
  selectCharacter(currentCharacter);

  function loadChromaKeyCanvas(source) {
    return new Promise(function (resolve, reject) {
      var image = new Image();
      image.addEventListener("load", function () {
        var canvas = document.createElement("canvas");
        canvas.width = image.naturalWidth || image.width;
        canvas.height = image.naturalHeight || image.height;
        var imageContext = canvas.getContext("2d", { willReadFrequently: true });

        try {
          imageContext.drawImage(image, 0, 0);
          var imageData = imageContext.getImageData(0, 0, canvas.width, canvas.height);
          var pixels = imageData.data;
          for (var offset = 0; offset < pixels.length; offset += 4) {
            if (pixels[offset] <= CHROMA_KEY_TOLERANCE &&
                pixels[offset + 1] >= 255 - CHROMA_KEY_TOLERANCE &&
                pixels[offset + 2] <= CHROMA_KEY_TOLERANCE) {
              pixels[offset] = 0;
              pixels[offset + 1] = 0;
              pixels[offset + 2] = 0;
              pixels[offset + 3] = 0;
            }
          }
          imageContext.putImageData(imageData, 0, 0);
          resolve(canvas);
        } catch (error) {
          reject(error);
        }
      });
      image.addEventListener("error", reject);
      image.src = source;
    });
  }

  function canvasToImageSource(canvas) {
    return new Promise(function (resolve) {
      if (canvas.toBlob && window.URL && window.URL.createObjectURL) {
        canvas.toBlob(function (blob) {
          resolve(blob ? window.URL.createObjectURL(blob) : canvas.toDataURL("image/png"));
        }, "image/png");
        return;
      }
      resolve(canvas.toDataURL("image/png"));
    });
  }

  function normalizeCharacter(character) {
    var normalized = String(character || "").toLowerCase();
    return normalized || DEFAULT_CHARACTER;
  }

  function mediaSource(character, routeName) {
    return "media/" + encodeURIComponent(character) + "/" + routeName + ASSET_VERSION;
  }

  function characterAssetsFor(character) {
    if (!characterAssets[character]) {
      characterAssets[character] = {
        backgrounds: {},
        handSource: null,
        leverTexture: null,
        started: false
      };
    }
    return characterAssets[character];
  }

  function showPreparedBackground(character, state, source) {
    var assets = characterAssetsFor(character);
    assets.backgrounds[state] = source;
    if (currentCharacter === character && lastBackgroundState === Number(state)) {
      backgroundImage.src = source;
      backgroundImage.classList.add("is-ready");
    }
  }

  function prepareBackgrounds(character) {
    var sequence = Promise.resolve();
    [1, 2, 3, 4].forEach(function (state) {
      var source = mediaSource(character, "bg" + state);
      sequence = sequence.then(function () {
        return loadChromaKeyCanvas(source)
          .then(canvasToImageSource)
          .then(function (preparedSource) {
            showPreparedBackground(character, state, preparedSource);
          })
          .catch(function () {
            showPreparedBackground(character, state, source);
          });
      });
    });
  }

  function applyCharacterAssets(character) {
    if (currentCharacter !== character) return;
    var assets = characterAssetsFor(character);
    if (assets.handSource) actionHand.src = assets.handSource;
    leverTexture = assets.leverTexture;
    leverTextureReady = Boolean(leverTexture);
    var backgroundSource = assets.backgrounds[lastBackgroundState];
    if (backgroundSource) {
      backgroundImage.src = backgroundSource;
      backgroundImage.classList.add("is-ready");
    }
  }

  function prepareCharacterAssets(character) {
    var assets = characterAssetsFor(character);
    if (assets.started) {
      applyCharacterAssets(character);
      return;
    }
    assets.started = true;
    prepareBackgrounds(character);

    var handSource = mediaSource(character, "hands");
    loadChromaKeyCanvas(handSource)
      .then(canvasToImageSource)
      .then(function (source) {
        assets.handSource = source;
        applyCharacterAssets(character);
      })
      .catch(function () {
        assets.handSource = handSource;
        applyCharacterAssets(character);
      });

    var leverSource = mediaSource(character, "hands-lever");
    loadChromaKeyCanvas(leverSource)
      .then(function (canvas) {
        assets.leverTexture = canvas;
        applyCharacterAssets(character);
      })
      .catch(function () {
        var fallbackTexture = new Image();
        fallbackTexture.addEventListener("load", function () {
          assets.leverTexture = fallbackTexture;
          applyCharacterAssets(character);
        });
        fallbackTexture.src = leverSource;
      });
  }

  function selectCharacter(character) {
    var selected = normalizeCharacter(character);
    if (currentCharacter === selected && characterAssetsFor(selected).started) return;
    currentCharacter = selected;
    artwork.dataset.character = selected;
    backgroundImage.src = TRANSPARENT_PIXEL;
    backgroundImage.classList.remove("is-ready");
    actionHand.src = TRANSPARENT_PIXEL;
    leverTexture = null;
    leverTextureReady = false;
    context.clearRect(0, 0, CANVAS_SIZE, CANVAS_SIZE);
    prepareCharacterAssets(selected);
    renderBackground(lastBackgroundState, true);
  }

  function clamp(value, minimum, maximum) {
    return Math.max(minimum, Math.min(maximum, value));
  }

  function normalizeVector(x, y) {
    var magnitude = Math.hypot(x, y);
    if (magnitude <= 1) return { x: x, y: y };
    return { x: x / magnitude, y: y / magnitude };
  }

  function defaultKeyBindings() {
    var bindings = {};
    KEY_ACTIONS.forEach(function (action) {
      bindings[action.id] = action.defaults.slice();
    });
    return bindings;
  }

  function defaultGamepadBindings() {
    var bindings = {};
    Object.keys(GAMEPAD_DEFAULTS).forEach(function (actionId) {
      bindings[actionId] = GAMEPAD_DEFAULTS[actionId].slice();
    });
    return bindings;
  }

  function actionForCode(code) {
    for (var index = 0; index < KEY_ACTIONS.length; index += 1) {
      var action = KEY_ACTIONS[index];
      if ((keyBindings[action.id] || []).indexOf(code) >= 0) return action;
    }
    return null;
  }

  function isKeyboardActionPressed(actionId) {
    var codes = keyBindings[actionId] || [];
    return codes.some(function (code) {
      return pressedKeyCodes.has(code) || relayPressedKeyCodes.has(code);
    });
  }

  function isLocalRelayOrigin() {
    var protocol = window.location.protocol || "";
    var hostname = window.location.hostname || "";
    return params.get("relay") !== "0" &&
      (protocol === "http:" || protocol === "https:") &&
      (hostname === "127.0.0.1" || hostname === "localhost");
  }

  function setInputRelayStatus(state, message) {
    inputRelayStatus.dataset.state = state;
    inputRelayStatus.textContent = message;
  }

  function normalizeRelayGamepad(gamepad) {
    if (!gamepad || !gamepad.connected) return null;
    var buttonValues = Array.isArray(gamepad.buttons) ? gamepad.buttons : [];
    return {
      id: gamepad.id || "OBS input relay controller",
      axes: Array.isArray(gamepad.axes) ? gamepad.axes : [0, 0],
      buttons: buttonValues.map(function (value) {
        var numericValue = Number(value) || 0;
        return { pressed: numericValue > 0.5, value: numericValue };
      })
    };
  }

  function applyRelayBindings(bindings) {
    if (!bindings || typeof bindings !== "object") return;
    var nextBindings = defaultKeyBindings();
    KEY_ACTIONS.forEach(function (action) {
      if (!Array.isArray(bindings[action.id])) return;
      nextBindings[action.id] = bindings[action.id].filter(function (code) {
        return typeof code === "string" && code.length > 0;
      });
    });
    keyBindings = nextBindings;
  }

  function applyRelayGamepadBindings(bindings) {
    if (!bindings || typeof bindings !== "object") return;
    var nextBindings = defaultGamepadBindings();
    Object.keys(GAMEPAD_DEFAULTS).forEach(function (actionId) {
      if (!Array.isArray(bindings[actionId])) return;
      nextBindings[actionId] = bindings[actionId].filter(function (control) {
        return typeof control === "string" && /^(Button\d+|Axis\d+[+-])$/.test(control);
      });
    });
    gamepadBindings = nextBindings;
  }

  function scheduleInputRelayPoll(delay) {
    window.setTimeout(pollInputRelay, delay);
  }

  function pollInputRelay() {
    window.fetch("input-state?time=" + Date.now(), { cache: "no-store" })
      .then(function (response) {
        if (!response.ok) throw new Error("Input relay returned " + response.status);
        return response.json();
      })
      .then(function (state) {
        selectCharacter(state.character);
        relayPressedKeyCodes = new Set(Array.isArray(state.keys) ? state.keys : []);
        relayGamepad = normalizeRelayGamepad(state.gamepad);
        applyRelayBindings(state.bindings);
        applyRelayGamepadBindings(state.gamepadBindings);
        setInputRelayStatus("connected", "OBS入力: 接続済み");
        scheduleInputRelayPoll(33);
      })
      .catch(function () {
        relayPressedKeyCodes.clear();
        relayGamepad = null;
        setInputRelayStatus("unavailable", "OBS入力: 補助サーバー未接続");
        scheduleInputRelayPoll(1200);
      });
  }

  function startInputRelay() {
    if (!isLocalRelayOrigin() || !window.fetch) {
      setInputRelayStatus("unavailable", "OBS入力: 通常ブラウザ入力");
      return;
    }
    setInputRelayStatus("checking", "OBS入力: 接続確認中");
    pollInputRelay();
  }

  function applyRadialDeadZone(x, y) {
    var magnitude = Math.hypot(x, y);
    if (magnitude <= AXIS_DEAD_ZONE) return { x: 0, y: 0 };

    var normalizedMagnitude = clamp(
      (magnitude - AXIS_DEAD_ZONE) / (1 - AXIS_DEAD_ZONE),
      0,
      1
    );

    return {
      x: (x / magnitude) * normalizedMagnitude,
      y: (y / magnitude) * normalizedMagnitude
    };
  }

  function isPressed(button) {
    return Boolean(button && (button.pressed || button.value > 0.5));
  }

  function getConnectedGamepad() {
    if (relayGamepad) return relayGamepad;
    if (navigator.getGamepads) {
      var pads = navigator.getGamepads();
      for (var index = 0; index < pads.length; index += 1) {
        if (pads[index]) return pads[index];
      }
    }
    return null;
  }

  function keyboardVector() {
    var x = Number(isKeyboardActionPressed("direction-right")) -
      Number(isKeyboardActionPressed("direction-left"));
    var y = Number(isKeyboardActionPressed("direction-down")) -
      Number(isKeyboardActionPressed("direction-up"));
    return normalizeVector(x, y);
  }

  function gamepadVector(gamepad) {
    if (!gamepad) return { x: 0, y: 0 };

    var x = gamepadActionValue("direction-right", gamepad) -
      gamepadActionValue("direction-left", gamepad);
    var y = gamepadActionValue("direction-down", gamepad) -
      gamepadActionValue("direction-up", gamepad);
    return applyRadialDeadZone(x, y);
  }

  function gamepadControlValue(control, gamepad) {
    var buttonMatch = /^Button(\d+)$/.exec(control);
    if (buttonMatch) {
      var button = gamepad.buttons[Number(buttonMatch[1])];
      return button ? Math.max(0, Math.min(1, Number(button.value) || Number(button.pressed))) : 0;
    }

    var axisMatch = /^Axis(\d+)([+-])$/.exec(control);
    if (axisMatch) {
      var axisValue = gamepad.axes && Number.isFinite(gamepad.axes[Number(axisMatch[1])])
        ? gamepad.axes[Number(axisMatch[1])]
        : 0;
      return axisMatch[2] === "+" ? Math.max(0, axisValue) : Math.max(0, -axisValue);
    }
    return 0;
  }

  function gamepadActionValue(actionId, gamepad) {
    var controls = gamepadBindings[actionId] || [];
    var value = 0;
    controls.forEach(function (control) {
      value = Math.max(value, gamepadControlValue(control, gamepad));
    });
    return value;
  }

  function mergeVectors(first, second) {
    return normalizeVector(first.x + second.x, first.y + second.y);
  }

  function interpolateHomography(target, amount) {
    var identity = [1, 0, 0, 0, 1, 0, 0, 0, 1];
    return target.map(function (value, index) {
      return identity[index] + (value - identity[index]) * amount;
    });
  }

  function projectHomographyPoint(matrix, x, y) {
    var divisor = matrix[6] * x + matrix[7] * y + matrix[8];
    return {
      x: (matrix[0] * x + matrix[1] * y + matrix[2]) / divisor,
      y: (matrix[3] * x + matrix[4] * y + matrix[5]) / divisor
    };
  }

  function projectLeverTexturePoint(matrix, x, y) {
    var point = projectHomographyPoint(matrix, x, y);
    var sourcePivot = projectHomographyPoint(
      matrix,
      LEVER_SOURCE_PIVOT.x,
      LEVER_SOURCE_PIVOT.y
    );
    return {
      x: LEVER_PIVOT.x + point.x - sourcePivot.x,
      y: LEVER_PIVOT.y + point.y - sourcePivot.y
    };
  }

  function projectLever(vector) {
    var magnitude = clamp(Math.hypot(vector.x, vector.y), 0, 1);
    if (magnitude === 0) {
      return {
        active: false,
        baseX: LEVER_PIVOT.x,
        baseY: LEVER_PIVOT.y
      };
    }

    var directionX = vector.x / magnitude;
    var directionY = vector.y / magnitude;
    var tilt = MAX_TILT_RADIANS * magnitude;
    var radialTravel = Math.sin(tilt) * STICK_LENGTH;
    var height = Math.cos(tilt) * STICK_LENGTH;
    var lateral = radialTravel * directionX;
    var forward = -radialTravel * directionY;
    var depthHorizontal = -Math.abs(forward);
    var forwardMirrorCorrection = directionY < 0
      ? 2 * (height - STICK_LENGTH) * -directionY
      : 0;
    var connectorX = LEVER_PIVOT.x +
      lateral * PANEL_RIGHT_AXIS.x +
      depthHorizontal * DEPTH_PROJECTION * PANEL_FORWARD_AXIS.x;
    var connectorY = LEVER_PIVOT.y - height +
      lateral * PANEL_RIGHT_AXIS.y +
      forward * DEPTH_PROJECTION * PANEL_FORWARD_AXIS.y +
      forwardMirrorCorrection;
    var isDirectForward = directionY < FRONT_ONLY_THRESHOLD;
    var isDirectBack = directionY > BACK_ONLY_THRESHOLD;
    var planarMatrix = null;
    if (isDirectForward) {
      planarMatrix = interpolateHomography(FRONT_HOMOGRAPHY, magnitude);
    } else if (isDirectBack) {
      planarMatrix = interpolateHomography(BACK_HOMOGRAPHY, magnitude);
    }
    if (planarMatrix) {
      var projectedConnector = projectLeverTexturePoint(
        planarMatrix,
        LEVER_SOURCE_CONNECTOR.x,
        LEVER_SOURCE_CONNECTOR.y
      );
      connectorX = projectedConnector.x;
      connectorY = projectedConnector.y;
    }
    var screenX = connectorX - LEVER_PIVOT.x;
    var screenY = connectorY - LEVER_PIVOT.y;
    var screenLength = Math.hypot(screenX, screenY);
    var screenAngle = Math.atan2(screenX, -screenY);
    var depthRatio = -directionY * Math.sin(tilt);
    var gripScale = 1 + depthRatio * DEPTH_SCALE;
    var gripCos = Math.cos(screenAngle);
    var gripSin = Math.sin(screenAngle);
    var gripA = gripCos * gripScale;
    var gripB = gripSin * gripScale;
    var gripC = -gripSin * gripScale;
    var gripD = gripCos * gripScale;
    var shaftScale = 1 + depthRatio * 0.05;
    return {
      active: true,
      baseX: LEVER_PIVOT.x,
      baseY: LEVER_PIVOT.y,
      connectorX: connectorX,
      connectorY: connectorY,
      length: screenLength,
      angle: screenAngle,
      gripA: gripA,
      gripB: gripB,
      gripC: gripC,
      gripD: gripD,
      shaftScale: shaftScale,
      planarMatrix: planarMatrix,
      magnitude: magnitude
    };
  }

  function drawProjectiveTriangle(source, target) {
    var sourceX1 = source[1].x - source[0].x;
    var sourceY1 = source[1].y - source[0].y;
    var sourceX2 = source[2].x - source[0].x;
    var sourceY2 = source[2].y - source[0].y;
    var targetX1 = target[1].x - target[0].x;
    var targetY1 = target[1].y - target[0].y;
    var targetX2 = target[2].x - target[0].x;
    var targetY2 = target[2].y - target[0].y;
    var divisor = sourceX1 * sourceY2 - sourceX2 * sourceY1;
    var a = (targetX1 * sourceY2 - targetX2 * sourceY1) / divisor;
    var b = (targetY1 * sourceY2 - targetY2 * sourceY1) / divisor;
    var c = (targetX2 * sourceX1 - targetX1 * sourceX2) / divisor;
    var d = (targetY2 * sourceX1 - targetY1 * sourceX2) / divisor;
    var e = target[0].x - a * source[0].x - c * source[0].y;
    var f = target[0].y - b * source[0].x - d * source[0].y;
    var centreX = (target[0].x + target[1].x + target[2].x) / 3;
    var centreY = (target[0].y + target[1].y + target[2].y) / 3;

    context.save();
    context.beginPath();
    target.forEach(function (point, index) {
      var distance = Math.hypot(point.x - centreX, point.y - centreY);
      var expansion = distance === 0 ? 1 : (distance + 0.6) / distance;
      var x = centreX + (point.x - centreX) * expansion;
      var y = centreY + (point.y - centreY) * expansion;
      if (index === 0) context.moveTo(x, y);
      else context.lineTo(x, y);
    });
    context.closePath();
    context.clip();
    context.transform(a, b, c, d, e, f);
    var left = Math.min(source[0].x, source[1].x, source[2].x);
    var top = Math.min(source[0].y, source[1].y, source[2].y);
    var right = Math.max(source[0].x, source[1].x, source[2].x);
    var bottom = Math.max(source[0].y, source[1].y, source[2].y);
    context.drawImage(
      leverTexture,
      left,
      top,
      right - left,
      bottom - top,
      left,
      top,
      right - left,
      bottom - top
    );
    context.restore();
  }

  function drawProjectiveCell(matrix, left, top, right, bottom) {
    var sourceTopLeft = { x: left, y: top };
    var sourceTopRight = { x: right, y: top };
    var sourceBottomRight = { x: right, y: bottom };
    var sourceBottomLeft = { x: left, y: bottom };
    var topLeft = projectLeverTexturePoint(matrix, left, top);
    var topRight = projectLeverTexturePoint(matrix, right, top);
    var bottomRight = projectLeverTexturePoint(matrix, right, bottom);
    var bottomLeft = projectLeverTexturePoint(matrix, left, bottom);

    drawProjectiveTriangle(
      [sourceTopLeft, sourceTopRight, sourceBottomLeft],
      [topLeft, topRight, bottomLeft]
    );
    drawProjectiveTriangle(
      [sourceTopRight, sourceBottomRight, sourceBottomLeft],
      [topRight, bottomRight, bottomLeft]
    );
  }

  function drawProjectiveLever(matrix) {
    var columns = 6;
    var rows = 8;
    var cellWidth = leverTexture.width / columns;
    var cellHeight = leverTexture.height / rows;
    for (var row = 0; row < rows; row += 1) {
      for (var column = 0; column < columns; column += 1) {
        drawProjectiveCell(
          matrix,
          column * cellWidth,
          row * cellHeight,
          (column + 1) * cellWidth,
          (row + 1) * cellHeight
        );
      }
    }
  }

  function drawLever(vector) {
    context.clearRect(0, 0, CANVAS_SIZE, CANVAS_SIZE);
    if (!leverTextureReady) return;

    var pose = projectLever(vector);
    if (!pose.active) return;

    if (pose.planarMatrix) {
      drawProjectiveLever(pose.planarMatrix);
      return;
    }

    context.save();
    context.translate(pose.baseX, pose.baseY);
    context.rotate(pose.angle);
    context.drawImage(
      leverTexture,
      SHAFT_CROP.x,
      SHAFT_CROP.y,
      SHAFT_CROP.width,
      SHAFT_CROP.height,
      -(SHAFT_CROP.width * pose.shaftScale) / 2,
      -pose.length,
      SHAFT_CROP.width * pose.shaftScale,
      pose.length
    );
    context.restore();

    context.save();
    context.translate(pose.connectorX, pose.connectorY);
    context.transform(pose.gripA, pose.gripB, pose.gripC, pose.gripD, 0, 0);
    context.drawImage(
      leverTexture,
      GRIP_CROP.x,
      GRIP_CROP.y,
      GRIP_CROP.width,
      GRIP_CROP.height,
      -GRIP_ANCHOR.x,
      -GRIP_ANCHOR.y,
      GRIP_CROP.width,
      GRIP_CROP.height
    );
    context.restore();
  }

  function readButtons(gamepad) {
    var active = new Set();

    KEY_ACTIONS.forEach(function (action) {
      if (action.kind === "button" && isKeyboardActionPressed(action.id)) {
        active.add(action.value);
      }
    });

    if (gamepad) {
      for (var modelIndex = 0; modelIndex < 8; modelIndex += 1) {
        if (gamepadActionValue("button-" + modelIndex, gamepad) > 0.5) {
          active.add(modelIndex);
        }
      }
    }
    return active;
  }

  function rememberNewPresses(buttons) {
    buttons.forEach(function (buttonIndex) {
      if (!previousButtons.has(buttonIndex)) {
        activationSequence += 1;
        activationOrder.set(buttonIndex, activationSequence);
      }
    });
    previousButtons = new Set(buttons);
  }

  function mostRecentButton(buttons) {
    var selected = -1;
    var selectedOrder = -1;
    buttons.forEach(function (buttonIndex) {
      var order = activationOrder.get(buttonIndex) || 0;
      if (order > selectedOrder) {
        selected = buttonIndex;
        selectedOrder = order;
      }
    });
    return selected;
  }

  function backgroundState(vector, buttons) {
    var leverActive = Math.hypot(vector.x, vector.y) > 0.001;
    var buttonActive = buttons.size > 0;
    if (leverActive && buttonActive) return 4;
    if (leverActive) return 3;
    if (buttonActive) return 2;
    return 1;
  }

  function renderBackground(state, force) {
    if (!force && state === lastBackgroundState) return;
    var backgroundSources = characterAssetsFor(currentCharacter).backgrounds;
    if (backgroundSources[state]) {
      backgroundImage.src = backgroundSources[state];
      backgroundImage.classList.add("is-ready");
    } else {
      backgroundImage.classList.remove("is-ready");
      backgroundImage.src = TRANSPARENT_PIXEL;
    }
    artwork.dataset.state = "bg" + state;
    lastBackgroundState = state;
  }

  function renderButton(buttons) {
    rememberNewPresses(buttons);
    var activeButton = mostRecentButton(buttons);
    if (activeButton === lastRenderedButton) return;

    if (activeButton < 0) {
      actionHand.classList.remove("is-pressing");
      actionHand.dataset.button = "";
    } else {
      var centre = BUTTON_CENTRES[activeButton];
      var left = ((centre.x - HAND_ALPHA_CENTRE.x) / CANVAS_SIZE) * 100;
      var top = ((centre.y - HAND_ALPHA_CENTRE.y) / CANVAS_SIZE) * 100;
      actionHand.style.setProperty("--hand-left", left.toFixed(6) + "%");
      actionHand.style.setProperty("--hand-top", top.toFixed(6) + "%");
      actionHand.dataset.button = String(activeButton + 1);
      actionHand.classList.add("is-pressing");
    }

    lastRenderedButton = activeButton;
  }

  function updateConnectionStatus(gamepad) {
    if (gamepad && announcedGamepad !== gamepad.id) {
      announcedGamepad = gamepad.id;
      connectionStatus.textContent = "接続中: " + gamepad.id;
      liveStatus.textContent = "ゲームパッドを接続しました";
    } else if (!gamepad && announcedGamepad) {
      announcedGamepad = "";
      connectionStatus.textContent = "ゲームパッド待機中";
      liveStatus.textContent = "ゲームパッドが切断されました";
    }
  }

  function updateDebug(vector, state) {
    if (debugPanel.hidden) return;
    inputState.textContent = "入力: x=" + vector.x.toFixed(2) +
      " y=" + vector.y.toFixed(2) + " / " + currentCharacter + " / bg" + state;
  }

  function frame() {
    var gamepad = getConnectedGamepad();
    var vector = mergeVectors(keyboardVector(), gamepadVector(gamepad));
    var buttons = readButtons(gamepad);
    var state = backgroundState(vector, buttons);

    currentVector = vector;
    renderBackground(state);
    drawLever(vector);
    renderButton(buttons);
    updateConnectionStatus(gamepad);
    updateDebug(vector, state);
    window.requestAnimationFrame(frame);
  }

  window.addEventListener("keydown", function (event) {
    var action = actionForCode(event.code);
    if (!action) return;
    event.preventDefault();
    pressedKeyCodes.add(event.code);
  });

  window.addEventListener("keyup", function (event) {
    if (pressedKeyCodes.has(event.code)) event.preventDefault();
    pressedKeyCodes.delete(event.code);
  });

  window.addEventListener("blur", function () {
    pressedKeyCodes.clear();
  });

  window.__pseudo3dLever = {
    project: function (x, y) {
      return projectLever(normalizeVector(x, y));
    },
    getVector: function () {
      return { x: currentVector.x, y: currentVector.y };
    },
    getCharacter: function () {
      return currentCharacter;
    }
  };

  window.requestAnimationFrame(frame);
})();
