/* Canvas artwork for the scene examples. World coordinates are metres on
   the horizontal plane. Screen coordinates use a common 520 × 334 view. */
(() => {
  const tau = 2 * Math.PI;
  const floor = { x: 260, y: 136, halfWidth: 217, halfDepth: 84 };

  function project(x, z, radius) {
    return {
      x: floor.x + (x / radius) * floor.halfWidth,
      y: floor.y - (z / radius) * floor.halfDepth
    };
  }

  function unproject(x, y, radius) {
    return {
      x: (x - floor.x) * radius / floor.halfWidth,
      z: (floor.y - y) * radius / floor.halfDepth
    };
  }

  function path(ctx, shape, fill, stroke, width = 1) {
    if (fill) {
      ctx.fillStyle = fill;
      ctx.fill(shape);
    }
    if (stroke) {
      ctx.lineWidth = width;
      ctx.strokeStyle = stroke;
      ctx.stroke(shape);
    }
  }

  function ellipse(ctx, x, y, rx, ry, fill, stroke, width = 1) {
    const shape = new Path2D();
    shape.ellipse(x, y, rx, ry, 0, 0, tau);
    path(ctx, shape, fill, stroke, width);
  }

  function line(ctx, x1, y1, x2, y2, colour, width = 1) {
    ctx.beginPath();
    ctx.moveTo(x1, y1);
    ctx.lineTo(x2, y2);
    ctx.strokeStyle = colour;
    ctx.lineWidth = width;
    ctx.stroke();
  }

  function stage(ctx, key, radius) {
    ctx.save();
    const field = ctx.createRadialGradient(260, 140, 0, 260, 140, 250);
    const tint = { crowd: '#a9cc9820', music: '#dda78521', swarm: '#88c8c323' };
    field.addColorStop(0, tint[key] ?? tint.crowd);
    field.addColorStop(1, '#a8c89a00');
    ctx.fillStyle = field;
    ctx.fillRect(0, 0, 520, 334);

    // A faint expanding wedge connects the visor in the foreground to the world.
    const viewCone = new Path2D('M244 269 L65 108 Q260 64 455 108 L276 269 Z');
    const wash = ctx.createLinearGradient(0, 266, 0, 90);
    wash.addColorStop(0, '#c1dbb60f');
    wash.addColorStop(1, '#c1dbb600');
    path(ctx, viewCone, wash);
    line(ctx, 244, 267, 97, 131, '#b9cbaa18');
    line(ctx, 276, 267, 423, 131, '#b9cbaa18');

    ctx.save();
    const boundary = new Path2D();
    boundary.ellipse(floor.x, floor.y, floor.halfWidth, floor.halfDepth, 0, 0, tau);
    ctx.clip(boundary);
    for (let i = -4; i <= 4; i++) {
      const distance = i * radius / 4;
      const x = project(distance, 0, radius).x;
      const y = project(0, distance, radius).y;
      const ink = i === 0 ? '#bed4ae40' : '#b4cba522';
      line(ctx, x, 43, x, 228, ink, .8);
      line(ctx, 38, y, 482, y, ink, .8);
    }
    ctx.restore();
    for (const fraction of [.5, 1]) {
      ellipse(ctx, floor.x, floor.y, floor.halfWidth * fraction, floor.halfDepth * fraction,
        null, fraction === 1 ? '#c7d9b662' : '#acbc9e30');
    }
    ctx.restore();
  }

  function facet(ctx, vertices, base, shade) {
    const face = new Path2D();
    vertices.forEach(([x, y], index) => index ? face.lineTo(x, y) : face.moveTo(x, y));
    face.closePath();
    path(ctx, face, base, base, .5);
    if (shade) path(ctx, face, shade);
  }

  function cube(ctx, x, y, r, colour) {
    const a = [x, y - r * 1.28];
    const b = [x + r, y - r * .73];
    const c = [x, y - r * .16];
    const d = [x - r, y - r * .73];
    const base = y + r * .5;
    facet(ctx, [a, b, c, d], colour, '#ffffff3b');
    facet(ctx, [d, c, [x, base + r * .57], [x - r, base]], colour, '#0000000c');
    facet(ctx, [c, b, [x + r, base], [x, base + r * .57]], colour, '#061a1457');
    line(ctx, d[0], d[1], c[0], c[1], '#ffffff86', .75);
    line(ctx, c[0], c[1], b[0], b[1], '#ffffff74', .75);
  }

  function pyramid(ctx, x, y, r, colour, heading) {
    // Project the four base corners and a forward-facing apex, then paint the
    // faces in depth order. Silhouette and facets indicate motion direction.
    const dx = Math.cos(heading);
    const dz = Math.sin(heading);
    const apex = [x + dx * 1.7 * r, y - dz * .9 * r];
    const base = [[1, 1], [1, -1], [-1, -1], [-1, 1]].map(([side, up]) => {
      const depth = -dz * .65 + dx * side * .72;
      return { xy: [x + r * (-dx * .65 - dz * side * .72), y - r * (depth * .5 + up * .58)], depth };
    });
    const faces = base.map((corner, i) => ({
      vertices: [corner.xy, base[(i + 1) % 4].xy, apex],
      depth: corner.depth + base[(i + 1) % 4].depth,
      shade: ['#17302438', '#15261a69', '#ffffff19', '#ffffff70'][i]
    }));
    // Rear-facing end cap belongs behind or in front of the side facets
    // according to the heading. Higher depth faces are farther away.
    faces.push({ vertices: base.map(corner => corner.xy), depth: -2 * dz, shade: '#0d261f4d' });
    faces.sort((a, b) => b.depth - a.depth);
    for (const face of faces) facet(ctx, face.vertices, colour, face.shade);
  }

  function sphere(ctx, x, y, r, colour) {
    ellipse(ctx, x, y, r, r, colour);
    const shading = ctx.createRadialGradient(x - r * .35, y - r * .42, r * .08, x, y, r);
    shading.addColorStop(0, '#ffffffb0');
    shading.addColorStop(.45, '#ffffff06');
    shading.addColorStop(1, '#06181694');
    ellipse(ctx, x, y, r, r, shading);
  }

  function entity(ctx, item, radius) {
    const {x, y} = project(item.x, item.z, radius);
    const cubeShape = item.shape === 'cube';
    const r = Math.max(cubeShape ? 8 : 5, item.size * floor.halfWidth / (2 * radius));
    const strength = Math.min(1, Math.max(0, item.glow || 0));
    ctx.save();
    if (item.halo) {
      const halo = item.halo * floor.halfWidth / radius;
      ctx.globalAlpha = .085 + strength * .095;
      ellipse(ctx, x, y, halo, halo * .63, item.haloColor || item.color);
      ctx.globalAlpha = .35 + strength * .5;
      ellipse(ctx, x, y, halo, halo * .63, null, item.haloColor || item.color);
      ctx.globalAlpha = 1;
    }
    ellipse(ctx, x + 1, y + r * 1.25, r * 1.3, r * .37, '#08120e68');
    if (strength > .08) {
      ctx.globalAlpha = .22 + strength * .4;
      ellipse(ctx, x, y + r * 1.04, r * (1.4 + strength * 1.1), r * (.52 + strength * .5),
        null, item.haloColor || item.color, 1.2);
      ctx.globalAlpha = 1;
    }
    if (cubeShape) cube(ctx, x, y, r, item.color);
    else if (item.shape === 'pyramid') pyramid(ctx, x, y, r, item.color, item.heading || 0);
    else sphere(ctx, x, y, r, item.color);
    ctx.restore();
  }

  const shell = new Path2D('M-42 -9 Q-41 -20 -29 -22 Q0 -26 29 -22 Q41 -20 42 -9 L40 3 Q38 11 30 12 L14 12 Q8 12 4 5 Q0 0 -4 5 Q-8 12 -14 12 L-30 12 Q-38 11 -40 3Z');
  const cushion = new Path2D('M-37 -9 Q-37 -16 -28 -17 Q0 -21 28 -17 Q37 -16 37 -9 L35 2 Q34 7 29 7 L16 7 Q11 7 7 1 Q1 -7 -5 -1 Q-12 7 -17 7 L-29 7 Q-34 7 -35 2Z');
  const topStrap = new Path2D('M-4 -21 Q-6 -31 -5 -35 Q0 -38 5 -35 Q6 -30 4 -21Z');
  const rearStrap = new Path2D('M-38 -9 C-52 24 -34 44 0 44 C34 44 52 24 38 -9');

  function headset(ctx) {
    ctx.save();
    ctx.translate(260, 271);
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    path(ctx, rearStrap, null, '#405449', 5.2);
    path(ctx, rearStrap, null, '#b9cab883', 1.1);
    path(ctx, topStrap, '#5a705e', '#b6c7b19c');
    const shellPaint = ctx.createLinearGradient(0, -24, 0, 13);
    shellPaint.addColorStop(0, '#ced9c9');
    shellPaint.addColorStop(.28, '#95a897');
    shellPaint.addColorStop(1, '#506958');
    path(ctx, shell, shellPaint, '#dce6d4', 1.15);
    path(ctx, cushion, '#25382e', '#647d68', 1.3);
    for (const eye of [-20, 20]) {
      const glass = ctx.createRadialGradient(eye - 3, -7, 1, eye, -4, 11);
      glass.addColorStop(0, '#9eb8a5');
      glass.addColorStop(.38, '#4c6a5d');
      glass.addColorStop(1, '#1c3026');
      ellipse(ctx, eye, -5, 10.6, 8.4, glass, '#a1b4a2a6', .85);
    }
    ctx.font = '500 13px Arial, sans-serif';
    ctx.fillStyle = '#c4d0be';
    ctx.fillText('Your headset', 57, 22);
    line(ctx, 36, 7, 52, 18, '#b3c6a977', .9);
    ctx.restore();
  }

  // The index fingertip is the anchor in both poses, so grabbing a cube
  // preserves its position under the user's pointer.
  const openHand = new Path2D('M-2 0 Q1 -2 3 2 L11 36 L14 14 Q15 9 18 10 Q22 11 21 17 L22 39 L25 22 Q26 17 29 18 Q33 19 32 24 L32 48 L35 36 Q37 31 40 33 Q42 34 41 39 L41 65 Q41 73 35 82 L32 91 L31 101 L8 101 L8 91 Q7 85 0 77 L-17 57 Q-21 51 -18 48 Q-15 45 -10 50 L2 60 L4 56 L-3 7 Q-5 2 -2 0Z');
  const pinchHand = new Path2D('M-2 0 Q3 -4 8 0 L22 17 Q25 20 25 25 L22 39 L25 34 Q28 29 32 32 L35 37 Q40 36 42 40 Q45 44 43 53 L40 72 Q38 79 34 84 L31 101 L8 101 L7 88 Q3 82 0 72 L-8 45 Q-11 39 -10 34 L-4 7 Q-2 2 0 3 Q5 5 4 11 L1 31 L8 42 L13 36 L16 24 L1 8 Q-5 3 -2 0Z');
  const openBones = [
    [[0,3],[3,18],[6,36],[8,54],[18,82]], [[18,13],[17,26],[17,41],[17,54],[18,82]],
    [[29,21],[28,32],[27,46],[26,57],[18,82]], [[39,35],[38,45],[36,56],[34,64],[18,82]],
    [[-16,51],[-8,59],[1,68],[18,82]]
  ];
  const pinchBones = [
    [[1,4],[10,12],[20,23],[16,43],[18,80]], [[28,34],[30,41],[28,54],[18,80]],
    [[35,38],[36,46],[33,60],[18,80]], [[40,43],[40,51],[36,65],[18,80]],
    [[1,8],[-5,34],[-1,49],[6,67],[18,80]]
  ];

  function hand(ctx, point, holding) {
    ctx.save();
    ctx.translate(point.x, point.y);
    ctx.rotate(-.31);
    ctx.scale(.73, .73);
    ctx.lineJoin = 'round';
    const skin = ctx.createLinearGradient(0, 0, 30, 101);
    skin.addColorStop(0, '#e8af8bbb');
    skin.addColorStop(.72, '#845b46a8');
    skin.addColorStop(1, '#483b3280');
    path(ctx, holding ? pinchHand : openHand, skin, '#ffd1aa', 1.65);
    for (const finger of holding ? pinchBones : openBones) {
      ctx.beginPath();
      finger.forEach(([x, y], index) => index ? ctx.lineTo(x, y) : ctx.moveTo(x, y));
      ctx.lineWidth = 1.1;
      ctx.strokeStyle = '#ffe0bbcc';
      ctx.stroke();
      for (const [x, y] of finger.slice(1,-1)) ellipse(ctx, x, y, 1.6, 1.6, '#ffddba');
    }
    ellipse(ctx, 0, 0, holding ? 8.6 : 5.4, holding ? 8.6 : 5.4, null, '#ffcf9e', 1.2);
    ctx.restore();
  }

  window.sceneArt = { stage, entity, headset, hand, project, unproject };
})();
