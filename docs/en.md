# 2D visibility

The library separates current visibility from persistent terrain knowledge. Isometric and top-down views use one cell grid. The shared `exploration_base` owns explored bits; this adapter never stores another copy in its save data.

`Fog2dProfile` identifies its RPG map by the same Id and contains a 1–64 cell sight radius, remembered brightness, additional opaque cells, initial exploration and object policies. Solid segments block sight by default. Low furniture and NPCs remain transparent unless explicitly configured. `WhenSolid` follows an ordinary door's effective state without a second fog-specific door state.

`Fog2dField.Compute` returns row-major visible indices y × width + x. The first opaque cell remains visible; cells behind it are hidden. Two closed edges stop a diagonal ray. Radius is measured between cell centres and clipped at map edges.

`Fog2dOcclusion` builds detached opacity for gameplay or previews without opening exploration memory. `Fog2dObserver` connects it to an ordinary RPG session and shared saves. Call Update after simulation changes and Dispose when leaving the map. Disposal clears current visibility while retaining explored terrain. Update reopens generation-bound memory after load. Observer settings are copied at construction; recreate it after changing an authored profile.

The adapter automatically applies a profile during ordinary RPG presentation, including renderer replacement. Maps without a profile retain their previous drawing and allocate no mask texture. The optional fog2d_editor provides map selection, sight settings, opacity rules, initial-reveal painting and undo through forms. The terrain-map screen is a separate domain.

The tile material supports an optional mask: red means explored, green means currently visible. Unknown artwork is concealed; explored cells outside sight are dimmed. An upright image's `TileVisual.VisibilityCell` samples its foot cell so unexplored cells behind its head do not cut the image. Flat materials and room pictures retain per-pixel sampling. Static quad planes without an explicit foot sample use their actual ground XY corners: wall height cannot move the top into unrelated cells, and long walls keep separate visibility along their width. Cropped source regions are accounted for.

`RpgWorldView.FrameFiltering` runs after ordinary frame evaluation, including a replacement renderer's first frame. The adapter submits separate pose storage without changing the world's cache. Visible sprites keep their exact interpolated position; remembered drawings are used only outside sight. They borrow the existing world texture cache without decoding artwork again. With no subscribers and the mask disabled, ordinary drawing remains unchanged.

`Fog2dWorld.MemoryOf(view)` borrows the requested view's current exploration handle, or returns null without a profile or while a loaded generation awaits reconciliation. `RefreshProfiles` detaches old masks after an authoring save; the ordinary next frame opens copied settings. Initial reveal does not reapply to an existing save. Native masks upload only when exploration or visibility changes and are released on view exit, mod unload and coordinated application shutdown.

`Fog2dHistory.Observe` records visible dynamic scenery. An unseen door state change stays concealed; seeing an object's previous footprint empty records its absence. An initially revealed area receives authored base scenery rather than its unseen active state. NPCs are never persisted as scenery. Recording copies the drawing without changing the gameplay object and retains the renderer's numeric quarantine for malformed parts.

The ordinary `RememberedScenes` mod slot stores only detached drawings and their last observed cells. Explored bits remain owned by `exploration_base`. Complete JSON validation and copying precede publication; an unsupported version leaves live history intact. New-game reset and mod unload clear old drawings. Resetting history alone never resets shared exploration.
