const scenes = {
  handbook: {
    label: "Planned resource",
    title: "From recording to figure",
    body: "The planned lab handbook will use two recorded sessions to show how to plot a hand’s path and measure how close it came to a virtual object.",
    note: "Readers will be able to follow the example online or repeat the analysis with the example data and code."
  },
  osc: {
    label: "Planned resource",
    title: "Connect your own software",
    body: "Open Sound Control (OSC) lets other programs exchange messages with SpatialMAS. For example, you could change a crowd’s speed from a Python script or use an object’s position in a Max sound patch.",
    note: "The planned example pack will include a connection check and a small scene you can control."
  },
  demo: {
    label: "Guided demos and pilots",
    title: "What would you like to explore?",
    body: "A guided demo can focus on a research question, a creative project or the next stage of a pilot. Share which scene interests you and whether you have access to a headset.",
    note: "This local preview shows the proposed demo enquiry. Contact routing will be added when the project mailboxes are ready."
  }
};

const swarmArt = document.querySelector("#swarm-art");
for (let index = 0; index < 65; index++) {
  const angle = index * 2.4;
  const radius = 20 + Math.sqrt(index / 65) * 77;
  const dot = document.createElementNS("http://www.w3.org/2000/svg", "circle");
  dot.setAttribute("cx", 180 + Math.cos(angle) * radius * 1.4);
  dot.setAttribute("cy", 112 + Math.sin(angle) * radius * .75);
  dot.setAttribute("r", 2 + index % 3);
  dot.setAttribute("fill", ["#c4d6ad", "#efb385", "#8eb8b2"][index % 3]);
  swarmArt.append(dot);
}

const dialog = document.querySelector("#detail");
document.querySelectorAll("[data-detail]").forEach(button => {
  button.addEventListener("click", () => {
    const detail = scenes[button.dataset.detail];
    for (const property of ["label", "title", "body", "note"]) {
      document.querySelector("#detail-" + property).textContent = detail[property];
    }
    dialog.showModal();
  });
});
dialog.querySelectorAll(".dialog-close").forEach(button => {
  button.addEventListener("click", () => dialog.close());
});
dialog.addEventListener("click", event => {
  if (event.target !== dialog) return;
  const bounds = dialog.getBoundingClientRect();
  if (event.clientX < bounds.left || event.clientX > bounds.right ||
      event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
});

