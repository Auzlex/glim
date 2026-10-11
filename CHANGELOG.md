
# v0.8.0

- Implemented Mesh Renderer component Cast Shadows option
  - Set to Off to prevent a mesh from casting shadows
  - Any other value then Off casts shadows from both sides
  - Useful for having lights inside meshes
- Implemented Light component No Shadows option
   - Previously all lights would cast shadows, even if the Shadow Type was set to No Shadows
- Much less visible seams, especially with directional lightmaps
- Allow renderers to contribute to baked lighting without taking up lightmap space at runtime
  - Toggle `Discard` on a lightmap group to skip saving it
  - Useful for using light probes or light volumes at runtime instead of lightmaps
  - This feature still requires proper lightmap UVs for best results, as it relies on the Meta pass for albedo and emission
  - Check https://github.com/z3y/glim/issues/19 for more details
- New Editor GUI for the GlimLightmapGroupSelector component
  - `New` button creates a new GlimLightmapGroup scriptable object in the current scene folder
  - Inlines the editor for the GlimLightmapGroup directly on the selector component for easier edits

# v0.7.0

- Added Ambient Occlusion baking
 - AO intensity and range parameters
 - Affects indirect light only
- Implemented faster GPU denoising
  - Open Image Denoise can now use other devices like CUDA, SYCL, HIP, METAL instead of only CPU
  - Logs currently selected device when baking
  - Results should be visually identical
- Added disc light type
- Implemented light cookies for Directional, Spot, Point, Rectangle and Disc lights
  - Directional cookie size
  - Colored cookie textures (uses only RGB channels)
  - This behavior does not match BIRP lights yet since they only sample alpha
  - Another difference is the spot cone attenuation is always applied, unlike in built-in
  - This behavior matches URP (and is likely more desirable anyways)
- Added a new component `GlimAdditionalLightSettings`
  - For additional settings for baking you can attach it to a Unity Light
  - Exposes the inner angle for spot lights and draws a gizmo for the Built-In render pipeline
  - Adds a Rectangle and Disc Light option for a cookie texture
- Implemented skybox reflection probe
  - Previously it was missing and was just sampling a default black cubemap
- Calculate ambient probe (the single global light probe that exists by default) with Unity apis instead of baking it in the lightmapper
  - This makes it sligtly more consistent, less hacky, and scales with the skybox intensity slider properly
- The skybox cubemap now uses the resolution set in the lighting window environment tab
- Added global multiplier for emissive materials
- Respect the stitch seams option on unity renderer components
- Matched the legacy light falloff of BIRP lights exactly
- Adjusted the light attenuation for Inverse Square mode to match URP lights
  - This results in lights being slightly brighter up close
- Added more tooltips
