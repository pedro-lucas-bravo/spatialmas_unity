// Planar browser examples of the two Unity scenarios. Distances are in metres.
(() => {
  const scenes = window.spatialScenes ??= {};
  const turn = 2 * Math.PI;
  const hertz = midi => 440 * 2 ** ((midi - 69) / 12);
  const distance = (a, b) => Math.hypot(a.x - b.x, a.z - b.z);

  function randomSequence(seed) {
    return () => {
      seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
      return seed / 4294967296;
    };
  }

  // The Unity scene has five freely moving spheres and two stationary, grabbable
  // cubes. Neighbour checks include every pair, including the two cubes.
  scenes.music = () => {
    const random = randomSequence(404);
    const radius = 1;
    const scales = [
      [0, 2, 4, 5, 6, 9],
      [0, 2, 4, 5, 7, 9, 11],
      [0, 2, 4, 7, 9],
      [0, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10, 5]
    ];
    const starts = [[-.60, .24], [.04, .53], [.46, .25], [-.21, -.17],
      [.15, -.44], [-.45, -.50], [.53, -.42]];
    const entities = starts.map(([x, z], id) => {
      const isCube = id >= 5;
      return {
        id, x, z, shape: isCube ? 'cube' : 'sphere', draggable: isCube,
        size: isCube ? .091 : .07, color: isCube ? '#f36957' : '#63dee0',
        halo: radius * .3, haloColor: isCube ? '#e55b53' : '#76cdd2',
        glow: 0, note: null
      };
    });
    const voices = entities.map(() => ({
      scale: scales[Math.floor(random() * scales.length)],
      octaves: 1 + Math.floor(random() * 3)
    }));
    const motions = entities.slice(0, 5).map(agent => ({
      heading: Math.atan2(agent.z, agent.x),
      speed: .65 * (.5 + random())
    }));

    function update(dt) {
      for (let i = 0; i < motions.length; i++) {
        const agent = entities[i];
        const motion = motions[i];
        if (dt > 0) {
          agent.x += Math.cos(motion.heading) * dt * motion.speed;
          agent.z += Math.sin(motion.heading) * dt * motion.speed;
          const extent = Math.hypot(agent.x, agent.z);
          if (extent >= radius) {
            motion.heading = Math.atan2(-agent.z, -agent.x) + (random() - .5) * Math.PI * .9;
            agent.x *= radius * .999 / extent;
            agent.z *= radius * .999 / extent;
          }
        }
      }
      for (const agent of entities) {
        const nearby = entities.some(other => other !== agent && distance(agent, other) < agent.halo);
        const cube = agent.draggable;
        agent.haloColor = cube ? (nearby ? '#ffd35e' : '#e55b53') : (nearby ? '#4486f0' : '#76cdd2');
        agent.glow = nearby ? .85 : 0;
        const { scale, octaves } = voices[agent.id];
        const bandCount = scale.length * octaves;
        const radialPosition = Math.hypot(agent.x, agent.z) / radius;
        const band = Math.min(bandCount - 1, Math.floor(radialPosition * bandCount));
        agent.note = nearby ? 48 + scale[band % scale.length] + 12 * Math.floor(band / scale.length) : null;
      }
    }
    update(0);
    return {
      entities, update,
      status(hand) {
        const held = entities.find(agent => agent.id === hand.held);
        if (held && held.note !== null) return 'The yellow ring marks an encounter. The cube holds a note.';
        if (held) return 'Bring the cube near another object to hold a note.';
        if (entities.some(agent => agent.draggable && agent.note !== null)) return 'A yellow ring shows which cube is holding a note.';
        return 'Drag a red cube close to another object to play a note.';
      },
      tone(hand) {
        const sounding = entities.filter(agent => agent.note !== null);
        const voice = sounding.find(agent => agent.id === hand.held)
          ?? sounding.find(agent => agent.draggable) ?? sounding[0];
        return voice ? { frequency: hertz(voice.note), interval: 0 } : null;
      }
    };
  };

  // This uses the compact layout's interaction length, phase coupling and
  // Active Phase Wave preset, projected onto the horizontal plane.
  scenes.swarm = () => {
    const random = randomSequence(101);
    const length = .5;
    const coupling = -.75;
    const notes = [60, 62, 64, 67, 69, 72, 74, 76, 79, 81];
    const cubeSpawns = [[-.42, -.44], [.48, -.16], [.02, .43]];
    const entities = Array.from({ length: 24 }, (_, id) => {
      const cube = id >= 21;
      const [x, z] = cube ? cubeSpawns[id - 21] : [(random() - .5) * .8, (random() - .5) * .8];
      return {
        id, x, z, shape: cube ? 'cube' : 'sphere', size: cube ? .07 : .056,
        draggable: cube, phase: random() * turn, naturalRate: 1.075 + random() * .25,
        color: '#80d1ba', glow: 0
      };
    });
    const changes = entities.map(() => ({ x: 0, z: 0, phase: 0 }));
    const colours = Array.from({ length: 360 }, (_, degree) => {
      const channel = n => {
        const k = (n + degree / 60) % 6;
        const value = .96 * (1 - .74 * Math.max(0, Math.min(k, 4 - k, 1)));
        return Math.round(255 * value).toString(16).padStart(2, '0');
      };
      return '#' + channel(5) + channel(3) + channel(1);
    });
    let ringing = 0;
    let frequency = 0;
    let movedCube = false;

    function update(dt, hand) {
      const step = Math.min(.04, dt) * 1.2;
      const average = 1 / (entities.length - 1);
      movedCube ||= hand.held != null;
      ringing = Math.max(0, ringing - dt);
      if (step) {
        for (const agent of entities) {
          const change = changes[agent.id];
          change.x = 0;
          change.z = 0;
          change.phase = agent.naturalRate;
          for (const other of entities) {
            if (agent === other) continue;
            const dx = other.x - agent.x;
            const dz = other.z - agent.z;
            const range2 = dx * dx + dz * dz + .015 * length * length;
            const range = Math.sqrt(range2);
            const offset = other.phase - agent.phase;
            const pull = (1 + Math.cos(offset) - length * length / range2) * average * length / range;
            change.x += dx * pull;
            change.z += dz * pull;
            change.phase += coupling * average * Math.sin(offset) * length / range;
          }
        }
      }
      for (const agent of entities) {
        const change = changes[agent.id];
        if (agent.id !== hand.held && step) {
          agent.x += change.x * step;
          agent.z += change.z * step;
          // Keep the projected example inside the small browser arena.
          const extent = Math.hypot(agent.x, agent.z);
          if (extent > 1) {
            agent.x /= extent;
            agent.z /= extent;
          }
        }
        const nextPhase = agent.phase + change.phase * step;
        agent.phase = (nextPhase + turn) % turn;
        agent.glow = Math.max(0, agent.glow - dt * 4);
        if (nextPhase >= turn) {
          const radialFraction = Math.min(1, Math.hypot(agent.x, agent.z) / (1.5 * length));
          frequency = hertz(notes[Math.round((1 - radialFraction) * (notes.length - 1))]);
          ringing = .12;
          agent.glow = 1;
        }
        agent.color = colours[Math.floor(agent.phase / turn * colours.length) % colours.length];
      }
    }
    update(0, { held: null });
    return {
      entities, update,
      status(hand) {
        if (hand.held != null) return 'The cube moves with your hand and still influences its neighbours.';
        if (movedCube) return 'The cube is moving with the group again. Colour follows each object’s rhythm.';
        return 'Drag a coloured cube. It shares its rhythm with the objects around it.';
      },
      tone() {
        return ringing > 0 ? { frequency, interval: .16 } : null;
      }
    };
  };
})();
