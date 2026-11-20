// Plan-view adaptation of the Unity compact Crowd Avoidance scene. Coordinates
// and velocities below use metres and metres per second in the arena plane.
(() => {
  const radius = 1.3;
  const cruiseSpeed = 0.65;
  const neighbourRange = 0.35 * radius;
  const separationRange = 0.14 * radius;
  const personalSpace = 0.65;
  const head = { x: 0, z: -0.95 };
  const handRange = personalSpace * 0.55;
  const chord = [50, 57, 60, 62, 64, 65, 69, 72, 74, 76];

  function proximityColour(amount) {
    const channels = [26 + 229 * amount, 209 - 127 * amount, 224 - 178 * amount];
    return '#' + channels.map(channel => Math.round(channel).toString(16).padStart(2, '0')).join('');
  }

  function createCrowd() {
    // Repeatable starting positions make it easier to compare the three examples.
    let seed = 42;
    function random() {
      seed = (Math.imul(1664525, seed) + 1013904223) >>> 0;
      return seed / 4294967296;
    }

    const entities = Array.from({ length: 32 }, (_, index) => {
      const angle = index * 2.399963 + random() * 0.24;
      const distance = radius * 0.75 * Math.sqrt((index + 0.5) / 32);
      const heading = random() * 2 * Math.PI;
      return {
        id: 'crowd-' + index, shape: 'pyramid', size: 0.064,
        x: Math.cos(angle) * distance, z: Math.sin(angle) * distance,
        vx: Math.cos(heading) * cruiseSpeed, vz: Math.sin(heading) * cruiseSpeed,
        heading, color: '#1ad1e0', glow: 0,
        pulseTimer: random() * 1.4, proximity: 0
      };
    });

    function addRepulsion(acceleration, agent, source, range, strength) {
      const dx = agent.x - source.x;
      const dz = agent.z - source.z;
      const d = Math.hypot(dx, dz);
      if (d >= range || d < 0.0001) return;
      const force = strength * Math.expm1((range - d) / (range * 0.35)) / d;
      acceleration.x += dx * force;
      acceleration.z += dz * force;
    }

    function stepAgent(agent, dt, hand) {
      let neighbours = 0;
      let centreX = 0, centreZ = 0, alignX = 0, alignZ = 0;
      const acceleration = { x: 0, z: 0 };
      for (const other of entities) {
        const dx = other.x - agent.x;
        const dz = other.z - agent.z;
        const d = Math.hypot(dx, dz);
        if (d < 0.0001 || d >= neighbourRange) continue;
        neighbours++;
        centreX += dx;
        centreZ += dz;
        alignX += other.vx;
        alignZ += other.vz;
        if (d < separationRange) {
          const push = 2.2 * (1 - d / separationRange) / d;
          acceleration.x -= dx * push;
          acceleration.z -= dz * push;
        }
      }
      if (neighbours) {
        const cohesion = cruiseSpeed / (Math.hypot(centreX, centreZ) || 1);
        const alignment = cruiseSpeed / (Math.hypot(alignX, alignZ) || 1);
        acceleration.x += 0.8 * (centreX * cohesion - agent.vx) + 1.1 * (alignX * alignment - agent.vx);
        acceleration.z += 0.8 * (centreZ * cohesion - agent.vz) + 1.1 * (alignZ * alignment - agent.vz);
      }
      const distance = Math.hypot(agent.x, agent.z);
      if (distance > radius * 0.85) {
        const inward = 6 * (distance - radius * 0.85) / distance;
        acceleration.x -= agent.x * inward;
        acceleration.z -= agent.z * inward;
      }
      addRepulsion(acceleration, agent, head, personalSpace, 1.5);
      addRepulsion(acceleration, agent, hand, handRange, 1.5 * 0.8);
      agent.vx += acceleration.x * dt;
      agent.vz += acceleration.z * dt;
      const speed = Math.hypot(agent.vx, agent.vz);
      const newSpeed = speed < 0.4 * cruiseSpeed ? cruiseSpeed : Math.min(speed, 2.2 * cruiseSpeed);
      if (speed > 0.0001) {
        agent.vx *= newSpeed / speed;
        agent.vz *= newSpeed / speed;
        agent.heading = Math.atan2(agent.vz, agent.vx);
      }
      agent.x += agent.vx * dt;
      agent.z += agent.vz * dt;
      // A planar projection can carry a fast agent past the visible arena.
      // Turn only its outward velocity back into the scene at that rim.
      const newDistance = Math.hypot(agent.x, agent.z);
      if (newDistance > radius) {
        const nx = agent.x / newDistance;
        const nz = agent.z / newDistance;
        agent.x = nx * radius;
        agent.z = nz * radius;
        const outward = Math.max(0, agent.vx * nx + agent.vz * nz);
        agent.vx -= 2 * outward * nx;
        agent.vz -= 2 * outward * nz;
        agent.heading = Math.atan2(agent.vz, agent.vx);
      }
      agent.proximity = Math.max(0, 1 - Math.hypot(agent.x - head.x, agent.z - head.z) / (personalSpace * 1.6));
      agent.color = proximityColour(agent.proximity);
      agent.glow = Math.max(0, agent.glow - dt * 3);
      agent.pulseTimer -= dt * (1 + agent.proximity * 4);
      if (agent.pulseTimer <= 0) {
        agent.pulseTimer = 0.6 + random() * 0.8;
        agent.glow = 0.12 + 0.56 * agent.proximity;
      }
    }

    return {
      entities,
      update(dt, hand) {
        // The original model integrates one steering update per headset frame.
        const step = Math.max(0, Math.min(dt, 0.05));
        for (const agent of entities) stepAgent(agent, step, hand);
      },
      status(hand) {
        return Math.hypot(hand.x, hand.z) < radius * 0.9
          ? 'Nearby pyramids steer around your hand as the crowd keeps moving.'
          : 'Reach into the crowd. The moving pyramids give your hand space.';
      },
      tone() {
        // A single representative pulse keeps the browser example intelligible.
        // Like Unity, head distance sets the pitch and pulse rate.
        const near = Math.max(...entities.map(agent => agent.proximity));
        const midi = chord[Math.round(near * (chord.length - 1))];
        return { frequency: 440 * 2 ** ((midi - 69) / 12), interval: 1 / (1 + 4 * near) };
      }
    };
  }

  window.spatialScenes ??= {};
  window.spatialScenes.crowd = createCrowd;
})();
