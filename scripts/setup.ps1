# TrpgVoiceDigest 首次运行环境初始化（Windows PowerShell）
$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$PyDir = Join-Path $ProjectRoot "python"
$VenvDir = Join-Path $PyDir "venv"

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host " TrpgVoiceDigest 环境初始化" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host ""

# ── 1. 检查 Python ──────────────────────────────────────
$pythonExe = $null
foreach ($name in @("python", "python3")) {
    $found = Get-Command $name -ErrorAction SilentlyContinue
    if ($found) {
        $pythonExe = $found.Source
        break
    }
}

if (-not $pythonExe) {
    Write-Host "X 未找到 Python。请从 https://www.python.org/downloads/ 下载并安装 Python 3.10+。" -ForegroundColor Red
    Write-Host "  安装时请勾选 'Add Python to PATH'。" -ForegroundColor Yellow
    exit 1
}

$pythonVersion = & $pythonExe --version 2>&1
Write-Host "V 已找到: $pythonVersion ($pythonExe)" -ForegroundColor Green
Write-Host ""

# ── 2. 检测 GPU ─────────────────────────────────────────
$hasCuda = $false
try {
    $nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($nvidiaSmi) {
        Write-Host "V 检测到 NVIDIA GPU（将安装 CUDA 版 PyTorch）" -ForegroundColor Green
        $hasCuda = $true
    }
} catch {
    Write-Host "i 未检测到 NVIDIA GPU，使用 CPU 模式" -ForegroundColor Yellow
}
Write-Host ""

# ── 3. 创建虚拟环境 ──────────────────────────────────────
if (-not (Test-Path $VenvDir)) {
    Write-Host ">>> 创建 Python 虚拟环境 ..." -ForegroundColor Cyan
    & $pythonExe -m venv $VenvDir
} else {
    Write-Host ">>> 复用已有虚拟环境: $VenvDir" -ForegroundColor Cyan
}

$venvPython = Join-Path $VenvDir "Scripts" "python.exe"
if (-not (Test-Path $venvPython)) {
    Write-Host "X 虚拟环境创建失败: 未找到 $venvPython" -ForegroundColor Red
    exit 1
}

& $venvPython -m pip install --upgrade pip --quiet
Write-Host ""

# ── 4. 安装依赖 ─────────────────────────────────────────
Write-Host ">>> 安装 WhisperX 及依赖（首次约需 2-5 分钟） ..." -ForegroundColor Cyan
if ($hasCuda) {
    & $venvPython -m pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu124
}
$reqPath = Join-Path $PyDir "requirements.txt"
& $venvPython -m pip install -r $reqPath
Write-Host ""

# ── 5. 确认捆绑的 ffmpeg ─────────────────────────────────
$bundledFfmpeg = Join-Path $ProjectRoot "tools" "ffmpeg" "ffmpeg.exe"
if (Test-Path $bundledFfmpeg) {
    Write-Host "V 已检测到捆绑的 ffmpeg: $bundledFfmpeg" -ForegroundColor Green
} else {
    $systemFfmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($systemFfmpeg) {
        Write-Host "V 使用系统的 ffmpeg: $($systemFfmpeg.Source)" -ForegroundColor Green
    } else {
        Write-Host "! 未找到 ffmpeg。录音功能将不可用。" -ForegroundColor Yellow
        Write-Host "  可前往 https://github.com/BtbN/FFmpeg-Builds/releases 下载。" -ForegroundColor Yellow
    }
}
Write-Host ""

# ── 6. 预下载 Whisper 模型（可选但推荐）───────────────────
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host " 预下载语音模型（可选）" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "模型约 1.5 GB，下载后首次转录无需等待。"
Write-Host "你可以在任何时候通过重新运行此脚本并添加 -DownloadModels 参数来下载。"
Write-Host ""

if ($args -contains "-DownloadModels") {
    Write-Host ">>> 正在下载 Whisper 模型（约需数分钟） ..." -ForegroundColor Cyan
    $downloadScript = @'
import whisperx
print("下载 Whisper turbo 模型 ...")
device = "cuda" if HAS_CUDA else "cpu"
compute_type = "float16" if HAS_CUDA else "int8"
model = whisperx.load_model("turbo", device=device, compute_type=compute_type)
print("模型下载完成")
'@ -replace "HAS_CUDA", (& { if ($hasCuda) { "True" } else { "False" } })
    try {
        & $venvPython -c $downloadScript
    } catch {
        Write-Host "! 模型下载失败。首次转录时将自动重试。" -ForegroundColor Yellow
    }
    Write-Host ""
}

# ── 7. 切换为 CPU 模式的提示 ─────────────────────────────
if (-not $hasCuda) {
    Write-Host "==============================================" -ForegroundColor Cyan
    Write-Host " CPU 模式配置提示" -ForegroundColor Cyan
    Write-Host "==============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "你的 config/app.config.json 中 Device 默认为 cuda。"
    Write-Host "由于未检测到 GPU，请将以下配置项改为："
    Write-Host ""
    Write-Host '  "Device": "cpu",' -ForegroundColor Yellow
    Write-Host '  "ComputeType": "int8",' -ForegroundColor Yellow
    Write-Host ""
}

# ── 8. 后续配置提示 ──────────────────────────────────────
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host " 后续手动配置" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. API Key 设置（以管理员身份运行 PowerShell）："
Write-Host ""
Write-Host "   [Environment]::SetEnvironmentVariable('OPENAI_API_KEY', 'sk-your-key-here', 'User')" -ForegroundColor Yellow
Write-Host ""
Write-Host "2. 说话人分离（可选）："
Write-Host "   a. 访问 https://huggingface.co/pyannote/speaker-diarization-3.1"
Write-Host "   b. 访问 https://huggingface.co/pyannote/segmentation-3.0"
Write-Host "   c. 访问 https://huggingface.co/settings/tokens 创建 Access Token"
Write-Host "   d. 设置环境变量："
Write-Host ""
Write-Host "   [Environment]::SetEnvironmentVariable('HF_TOKEN', 'hf_your_token_here', 'User')" -ForegroundColor Yellow
Write-Host ""
Write-Host "3. 启动应用："
Write-Host ""
Write-Host "   双击 TrpgVoiceDigest.Gui.exe"
Write-Host ""
Write-Host "==============================================" -ForegroundColor Cyan
Write-Host " 环境初始化完成" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan
