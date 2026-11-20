# SpatialMAS website design preview

Run `python -m http.server 4321 --bind 127.0.0.1` in this repository. Use the scene selector to try Crowd Avoidance, Musical Encounters or Swarmalators. Guide the hand with a pointer, touch or arrow keys. In the musical and swarm scenes, drag a cube or use Space to grab and release one. Each scene includes a guided gesture. Audio is optional. `identity.html` contains the design sheet.

The browser examples approximate the Unity scenes on a horizontal plane. Crowd Avoidance uses the compact layout (32 velocity-oriented pyramids, 1.3 m arena radius, 0.65 m/s base speed), with separate head and hand repulsion. Music and Swarmalators provide grabbable cubes. Sound is represented by a single voice. The headset at the front indicates the participant viewpoint. The separate figure `recorded-reach.svg` shows measured distances to the nearest agent centre in the two Crowd Avoidance recording folders dated 2026-11-16 in the Unity repository. The analysis uses tracked right-hand and agent positions at matching session and sample IDs. See `ExternalTools/crowd_navigation_example/compare_crowd_recordings.ipynb` for the reference analysis.

The handbook, OSC pack and demo enquiry are previews. The proposed support mailbox is shown as text until configured. Public hosting and mail delivery are separate from this local repository.

## Institutional artwork

- University of Oslo, full English white logo: https://www.uio.no/om/designmanual/profilelementer/logo/formell-logo-engelsk/03_uio_full_logo_eng_neg.svg
- Research Council of Norway, English funded-project badge, white: https://identitet.forskningsradet.no/verktoykasse/logo (funding badge download, English negative SVG).
- The 2026 qualification award listing identifies UiO and project 365744: https://www.forskningsradet.no/en/call-for-proposals/2026/qualification-research-commercialisation-publicly-funded-research/

The local sketch assumes spatialmas.com is the product site. If the University is the formal publisher, align the masthead and page profile with its [collaboration guidance](https://www.uio.no/english/about/designmanual/brand-architecture/collaborations/index.html).

