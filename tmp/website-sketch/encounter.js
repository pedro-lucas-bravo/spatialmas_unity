// Render browser-scale examples using a shared hand and a fixed headset viewpoint.
(() => {
  const stage = document.querySelector('#encounter-stage');
  const canvas = document.querySelector('#encounter-canvas');
  const ctx = canvas.getContext('2d');
  const art = window.sceneArt;
  const examples = document.querySelector('#scene-examples');
  const sceneButtons = [...document.querySelectorAll('[data-scene]')];
  const actionButton = document.querySelector('#encounter-reach');
  const motionButton = document.querySelector('#encounter-motion');
  const soundButton = document.querySelector('#encounter-sound');
  const caption = document.querySelector('#encounter-status');
  const hint = document.querySelector('#reach-guide');
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
  const settings = {
    crowd: {
      name: 'Crowd Avoidance', radius: 1.3, action: 'Try a reach', undo: 'Withdraw hand',
      hint: 'Guide the hand with your pointer or touch. The pyramids steer around it. Listen for a faster pulse from those nearest your headset.'
    },
    music: {
      name: 'Musical Encounters', radius: 1, action: 'Bring cubes together', undo: 'Separate cubes',
      hint: 'Drag a red cube close to another object. Its halo turns yellow and a note sounds. Move it away to hear the release.'
    },
    swarm: {
      name: 'Swarmalators', radius: 1, action: 'Move a cube', undo: 'Move it back',
      hint: 'Drag a coloured cube to a new spot. Watch the group rearrange as neighbours influence one another’s movement and rhythm.'
    }
  };
  const view = { scale: 1, left: 0, top: 0, dpr: 1 };
  const hand = { x: 0, z: 0, held: null };
  let key, scene, radius;
  let guide = null;
  let restore = null;
  let pointerId = null;
  let paused = reducedMotion.matches;
  let visible = true;
  let frameId = 0;
  let previousTime = 0;

  // A held note for Musical Encounters, or one representative pulse voice.
  const AudioEngine = window.AudioContext || window.webkitAudioContext;
  let audio, oscillator, gain;
  let sound = false;
  let nextBeat = 0;
  let lastNote = null;
  function silence() {
    if (gain) {
      gain.gain.cancelScheduledValues(audio.currentTime);
      gain.gain.setTargetAtTime(0, audio.currentTime, .014);
    }
    nextBeat = 0;
    lastNote = null;
  }
  function playTone(time) {
    if (!sound || audio?.state !== 'running') return;
    const tone = scene.tone(hand);
    if (!tone || !Number.isFinite(tone.frequency) || tone.frequency <= 0) {
      if (lastNote !== null) silence();
      return;
    }
    const now = audio.currentTime;
    oscillator.frequency.setTargetAtTime(tone.frequency, now, .018);
    if (!tone.interval) {
      if (lastNote !== tone.frequency) gain.gain.setTargetAtTime(.045, now, .035);
    } else if (time >= nextBeat) {
      gain.gain.cancelScheduledValues(now);
      gain.gain.setValueAtTime(.0001, now);
      gain.gain.exponentialRampToValueAtTime(.065, now + .009);
      gain.gain.exponentialRampToValueAtTime(.0001, now + Math.min(.18, tone.interval * .8));
      nextBeat = time + tone.interval * 1000;
    }
    lastNote = tone.frequency;
  }
  function updateSoundButton() {
    soundButton.textContent = sound ? 'Sound on' : 'Sound off';
    soundButton.setAttribute('aria-pressed', String(sound));
  }
  soundButton.hidden = !AudioEngine;
  soundButton.addEventListener('click', async () => {
    sound = !sound;
    updateSoundButton();
    if (!sound) { silence(); return; }
    try {
      if (!audio) {
        audio = new AudioEngine();
        oscillator = audio.createOscillator();
        oscillator.type = 'triangle';
        gain = audio.createGain();
        gain.gain.value = 0;
        oscillator.connect(gain).connect(audio.destination);
        oscillator.start();
      }
      await audio.resume();
      invalidate();
    } catch {
      sound = false;
      updateSoundButton();
      soundButton.textContent = 'Sound unavailable';
    }
  });

  function invalidate() {
    if (!frameId && visible && !document.hidden) frameId = requestAnimationFrame(draw);
  }
  function renderStage() {
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.setTransform(view.scale * view.dpr, 0, 0, view.scale * view.dpr,
                     view.left * view.dpr, view.top * view.dpr);
    art.stage(ctx, key, radius);
    const ordered = scene.entities.slice().sort((a, b) => b.z - a.z);
    for (const entity of ordered) art.entity(ctx, entity, radius);
    art.headset(ctx);
    art.hand(ctx, art.project(hand.x, hand.z, radius), hand.held !== null);
  }
  function resize() {
    const {width, height} = stage.getBoundingClientRect();
    if (!width || !height) return;
    view.dpr = Math.min(2, devicePixelRatio || 1);
    view.scale = Math.min(width / 520, height / 334);
    view.left = (width - 520 * view.scale) / 2;
    view.top = (height - 334 * view.scale) / 2;
    canvas.width = Math.round(width * view.dpr);
    canvas.height = Math.round(height * view.dpr);
    invalidate();
  }
  function moveHand(x, z) {
    const distance = Math.hypot(x, z);
    const multiplier = Math.min(1, radius / Math.max(.001, distance));
    hand.x = x * multiplier;
    hand.z = z * multiplier;
    if (hand.held !== null) {
      const cube = scene.entities.find(e => e.id === hand.held);
      if (cube) { cube.x = hand.x; cube.z = hand.z; }
    }
    invalidate();
  }
  function animateGuide(time) {
    if (!guide) return;
    const fraction = reducedMotion.matches ? 1 : Math.min(1, (time - guide.start) / 550);
    const smooth = fraction * fraction * (3 - 2 * fraction);
    moveHand(guide.from.x + (guide.to.x - guide.from.x) * smooth,
             guide.from.z + (guide.to.z - guide.from.z) * smooth);
    if (fraction === 1) {
      hand.held = null;
      guide = null;
    }
  }
  function draw(time) {
    frameId = 0;
    const elapsed = previousTime ? Math.min(.05, (time - previousTime) / 1000) : 0;
    previousTime = time;
    animateGuide(time);
    scene.update(paused ? 0 : elapsed, hand);
    renderStage();
    const message = scene.status(hand);
    if (caption.textContent !== message) caption.textContent = message;
    playTone(time);
    if (!paused || guide || sound) invalidate();
  }

  function releasePointer() {
    if (pointerId !== null && stage.hasPointerCapture(pointerId)) stage.releasePointerCapture(pointerId);
    pointerId = null;
    hand.held = null;
    delete stage.dataset.cursor;
  }
  function cancelExample() {
    if (guide) hand.held = null;
    guide = null;
    restore = null;
    actionButton.textContent = settings[key].action;
  }
  function selectScene(next) {
    silence();
    releasePointer();
    key = next;
    scene = window.spatialScenes[key]();
    radius = settings[key].radius;
    cancelExample();
    hand.x = -.32 * radius;
    hand.z = -.91 * radius;
    hint.textContent = settings[key].hint;
    stage.setAttribute('aria-label', settings[key].name + ', interactive example');
    stage.dataset.grabbable = String(scene.entities.some(e => e.draggable));
    sceneButtons.forEach(b => b.setAttribute('aria-pressed', String(b.dataset.scene === key)));
    previousTime = 0;
    invalidate();
  }
  sceneButtons.forEach(b => b.addEventListener('click', () => {
    if (b.dataset.scene !== key) selectScene(b.dataset.scene);
  }));
  document.querySelectorAll('[data-open-scene]').forEach(b => b.addEventListener('click', () => {
    selectScene(b.dataset.openScene);
    sceneButtons.find(item => item.dataset.scene === key).focus({preventScroll:true});
    examples.scrollIntoView({block:'start', behavior: reducedMotion.matches ? 'instant' : 'smooth'});
  }));

  function cursorPoint(event) {
    const rect = stage.getBoundingClientRect();
    return {x:(event.clientX - rect.left - view.left) / view.scale,
            y:(event.clientY - rect.top - view.top) / view.scale};
  }
  function nearestCube(point, limit = Infinity) {
    let closest = null;
    let best = limit;
    for (const cube of scene.entities) {
      if (!cube.draggable) continue;
      const pos = art.project(cube.x, cube.z, radius);
      const gap = Math.hypot(pos.x - point.x, pos.y - point.y);
      if (gap < best) { best = gap; closest = cube; }
    }
    return closest;
  }
  function pointAt(point) {
    const world = art.unproject(point.x, point.y, radius);
    moveHand(world.x, world.z);
  }
  stage.addEventListener('pointerdown', event => {
    if (!event.isPrimary || event.button !== 0) return;
    cancelExample();
    const point = cursorPoint(event);
    const cube = nearestCube(point, Math.max(15, 22 / view.scale));
    hand.held = cube?.id ?? null;
    pointerId = event.pointerId;
    stage.setPointerCapture(event.pointerId);
    stage.focus({preventScroll:true});
    if (cube) stage.dataset.cursor = 'grabbing';
    pointAt(point);
  });
  stage.addEventListener('pointermove', event => {
    if (!event.isPrimary || (pointerId !== null && pointerId !== event.pointerId)) return;
    // A guided gesture completes even if scrolling places the pointer over it.
    // Pressing the scene takes control immediately through pointerdown.
    if (guide) return;
    const point = cursorPoint(event);
    stage.dataset.cursor = hand.held !== null ? 'grabbing' : nearestCube(point, 20 / view.scale) ? 'grab' : '';
    pointAt(point);
  });
  function endDrag(event) {
    if (pointerId === null || event.pointerId !== pointerId) return;
    releasePointer();
    invalidate();
  }
  stage.addEventListener('pointerup', endDrag);
  stage.addEventListener('pointercancel', endDrag);
  stage.addEventListener('lostpointercapture', endDrag);
  stage.addEventListener('blur', () => {
    if (pointerId === null && !guide) hand.held = null;
    invalidate();
  });
  stage.addEventListener('keydown', event => {
    const directions = {ArrowUp:[0,1], ArrowDown:[0,-1], ArrowLeft:[-1,0], ArrowRight:[1,0]};
    const delta = directions[event.key];
    if (!delta && ![' ', 'Enter', 'Escape'].includes(event.key)) return;
    event.preventDefault();
    cancelExample();
    if (delta) {
      moveHand(hand.x + delta[0] * radius * .08, hand.z + delta[1] * radius * .08);
    } else if (!event.repeat) {
      if (hand.held !== null || event.key === 'Escape') hand.held = null;
      else {
        const cube = nearestCube(art.project(hand.x, hand.z, radius));
        if (cube) { hand.held = cube.id; moveHand(cube.x, cube.z); }
      }
      invalidate();
    }
  });

  actionButton.addEventListener('click', () => {
    releasePointer();
    let cube = null;
    let to;
    if (restore) {
      cube = scene.entities.find(e => e.id === restore.id);
      to = restore.point;
      restore = null;
    } else {
      const cubes = scene.entities.filter(e => e.draggable);
      cube = cubes[0];
      const source = cube ?? hand;
      restore = { id: cube?.id, point: {x:source.x, z:source.z} };
      if (key === 'music') to = { x:cubes[1].x - .12, z:cubes[1].z + .05 };
      else if (key === 'swarm') to = { x:source.x >= 0 ? -.55 : .55, z:.32 };
      else to = { x:.06, z:.03 };
    }
    if (cube) { hand.held = cube.id; moveHand(cube.x, cube.z); }
    guide = { from:{x:hand.x,z:hand.z}, to, start:performance.now() };
    actionButton.textContent = restore ? settings[key].undo : settings[key].action;
    invalidate();
  });
  function updateMotion() {
    motionButton.textContent = paused ? 'Resume motion' : 'Pause motion';
    motionButton.setAttribute('aria-pressed', String(paused));
    previousTime = 0;
    invalidate();
  }
  motionButton.onclick = () => { paused = !paused; updateMotion(); };
  reducedMotion.addEventListener('change', event => { paused = event.matches; updateMotion(); });
  function visibilityChanged() {
    previousTime = 0;
    if (visible && !document.hidden) invalidate();
    else {
      cancelAnimationFrame(frameId);
      frameId = 0;
      silence();
    }
  }
  new IntersectionObserver(entries => {
    visible = entries[0].isIntersecting;
    visibilityChanged();
  }).observe(examples);
  document.addEventListener('visibilitychange', visibilityChanged);
  new ResizeObserver(resize).observe(stage);
  selectScene('crowd');
  updateMotion();
  resize();
})();
