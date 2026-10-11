# Drawing with ComfyUI

The app can draw through a ComfyUI server on your own machine. Add an image model on the **Models** page
from the **ComfyUI (local)** preset. The base URL is `http://127.0.0.1:8188`, ComfyUI's own
port; if yours listens elsewhere (some ComfyUI Desktop installs use `8000`), change it on the profile. The model id names the workflow set.

## The shipped workflow set: `flux-dev-nunchaku`

FLUX.1 in Nunchaku's 4-bit SVDQuant form, set up for a 20-series GPU (`float16`, `nunchaku-fp16`
attention, CPU offload when there is less than 14 GB of VRAM). It lives in
`src/RoleplayStudio.AI/Images/ComfyUI/Workflows/flux-dev-nunchaku/`:

| File | Used for |
|---|---|
| `portrait.json` | A character's portrait, which becomes their reference image (FLUX.1 dev, 24 steps) |
| `scene.json` | A picture of the chat with no reference portraits (FLUX.1 dev, 20 steps) |
| `references.json` | A picture drawn after up to three reference portraits (FLUX.1 Kontext dev, 20 steps) |

### Models to download

Into ComfyUI's `models` folder (`Documents\ComfyUI\models` for ComfyUI Desktop):

| Folder | File | From |
|---|---|---|
| `diffusion_models` | `svdq-int4_r32-flux.1-dev.safetensors` | huggingface.co/nunchaku-tech/nunchaku-flux.1-dev |
| `diffusion_models` | `svdq-int4_r32-flux.1-kontext-dev.safetensors` | huggingface.co/nunchaku-tech/nunchaku-flux.1-kontext-dev |
| `text_encoders` | `clip_l.safetensors`, `t5xxl_fp8_e4m3fn_scaled.safetensors` | huggingface.co/comfyanonymous/flux_text_encoders |
| `vae` | `ae.safetensors` | huggingface.co/black-forest-labs/FLUX.1-dev (accept the licence first) |

The ComfyUI-nunchaku node needs its Python wheel too: run its `install_wheel.json` example workflow
once, then restart ComfyUI.

## The shipped workflow set: `juggernaut-xl`

Juggernaut XL, an SDXL checkpoint: lighter than FLUX on a 6 GB card, with a negative prompt, drawn
with DPM++ 2M SDE Karras at CFG 4.5. In `Workflows/juggernaut-xl/`:

| File | Used for |
|---|---|
| `portrait.json` | A character's portrait (35 steps) |
| `scene.json` | A picture of the chat with no reference portrait (30 steps) |
| `references.json` | A picture drawn after one reference portrait, through IP-Adapter Plus Face at weight 0.7 |

IP-Adapter steers the whole picture towards one face, so the set takes a single reference portrait:
in a picture of several people it is the first one's, and the others may come out looking like them.

### Models and nodes to install

| Folder | File | From |
|---|---|---|
| `checkpoints` | `juggernautXL_ragnarok.safetensors` | civitai.com/models/133005 (Juggernaut XL, Ragnarok) |
| `ipadapter` | `ip-adapter-plus-face_sdxl_vit-h.safetensors` | huggingface.co/h94/IP-Adapter (`sdxl_models`) |
| `clip_vision` | `CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors` | huggingface.co/h94/IP-Adapter (`models/image_encoder/model.safetensors`, renamed) |

The reference workflow needs the **ComfyUI_IPAdapter_plus** custom node (install it from the
Manager). Another Juggernaut version works too: change `ckpt_name` in all three files to its file name.

## Writing or changing a workflow

A workflow is ComfyUI's API format (**Workflow → Export (API)**), with these conventions:

- The picture is read from the node with id **`output`** (a `PreviewImage`, so nothing piles up in
  ComfyUI's output folder).
- Placeholders are whole input values: `{{prompt}}`, `{{negative}}`, `{{width}}`, `{{height}}`,
  `{{seed}}`, and `{{reference1}}`, `{{reference2}}`, … for the uploaded reference portraits.
- Nodes of reference slot N have the id `referenceN` or start with `referenceN_`. Slots without a
  portrait are removed. Whatever read from a removed node reads that node's `conditioning` input
  instead, or its `model` input when it has none, so chain the slots through one of those
  (`ReferenceLatent` through `conditioning`, IP-Adapter through `model`).
- A set needs `portrait.json` and `scene.json`; `references.json` is optional. A set without it draws
  without references.

A new folder beside `flux-dev-nunchaku` is a new set; it is embedded at build time, so rebuild and
restart the app after changing one.
