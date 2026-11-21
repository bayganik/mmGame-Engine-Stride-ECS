mmFbxViewer — drop your models here
====================================

Put an FBX file (or OBJ, DAE, 3DS, BLEND, X, MD2, MD3, DXF) in this
folder, together with its PNG/JPG textures in the same layout the
model was exported with (usually the textures sit right next to the
FBX, or in a subfolder the FBX references). Models can also live
in subfolders up to 2 levels deep — Assets/fort/*.fbx, or
Assets/fort/walls/*.fbx — and the menu lists them with their
folder, so two models with the same file name stay distinct.

The viewer opens on a menu listing every model in this folder —
click one (or press its number key) to view it. Esc returns to
the menu from a model, and quits from the menu itself.

The viewer scans this folder when it starts (F5 rescans). It loads
the models alphabetically; every mesh in the model becomes one
piece you can page through.

Keys
----
Left / Right (or PgUp / PgDn)  previous / next piece
Up / Down                      previous / next model file
T                              toggle: single piece / whole model assembled
F                              toggle: orbit camera / fly camera
W A S D                        fly: move (forward / left / back / right)
Q / E                          fly: down / up (Shift = 3x faster)
Left-drag                      orbit: orbit around the piece · fly: look around
Mouse wheel                    orbit: zoom · fly: dolly forward/back
R                              re-fit the camera to the current piece
F5                             rescan the Assets folder
Esc                            back to menu (quits from the menu)

Notes
-----
- The viewer normalizes each model's scale so it frames the same
  way no matter what units the file was authored in (cm, m, mm).
  The file's own span is shown in the top HUD line.
- A piece with no texture renders in its material's diffuse color
  (grey when the file carries none).
- Textures are matched by the path stored in the model file,
  resolved against this folder. If a texture shows up missing,
  check the file name matches the one the FBX references.
- Some FBX files link no color texture at all (the modular fort
  links only normal + roughness maps). For those, the viewer
  falls back to a color map in this folder named after the
  material, e.g. modular_fort_01_wall_4k.png for material
  modular_fort_01_wall_1. Normal/roughness/metal maps are never
  picked as the color map.
