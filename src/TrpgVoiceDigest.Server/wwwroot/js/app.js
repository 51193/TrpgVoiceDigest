// ===== State =====
const speakerSide = {};
let sideNext = 'left';
const speechColors = [
    '#e94560', '#4caf50', '#2196f3', '#ff9800', '#9c27b0',
    '#00bcd4', '#ff5722', '#607d8b', '#e91e63', '#3f51b5'
];
const speakerColors = {};

// ===== SignalR Connection =====
const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hub/sync')
    .withAutomaticReconnect()
    .build();

connection.on('FileUpdated', onFilesUpdated);

connection.onreconnecting(() => {
    console.log('Reconnecting...');
});

connection.onreconnected(() => {
    console.log('Reconnected');
});

connection.start().catch(err => console.error('SignalR connection error:', err));

// ===== File Update Handler =====
function onFilesUpdated(files) {
    if (files['refinement.md']) {
        renderRefinement(files['refinement.md']);
    }
    if (files['story_progress.md']) {
        renderStoryProgress(files['story_progress.md']);
    }
    if (files['tasks.md']) {
        renderTasks(files['tasks.md']);
    }
}

// ===== Render Refinement as Chat =====
function renderRefinement(md) {
    const chatArea = document.getElementById('chat-area');
    const wasAtBottom = isAtBottom(chatArea);

    chatArea.innerHTML = '';

    const lines = md.split('\n').filter(l => l.trim());
    if (lines.length === 0) { showPlaceholder(chatArea); return; }

    // Extract title for header (optional), skip for display
    const contentLines = lines.filter(l => !/^#\s/.test(l.trim()) && !/^暂无内容/.test(l.trim()));
    if (contentLines.length === 0) { showPlaceholder(chatArea); return; }

    let lastSpeaker = null;

    for (const line of contentLines) {
        const match = line.match(/^(.+?)[：:](.+)/);
        if (!match) {
            if (line.trim()) {
                addSceneBubble(chatArea, line.trim());
            }
            lastSpeaker = null;
            continue;
        }

        const speaker = match[1].trim();
        const text = match[2].trim();

        if (speaker === '[场景]') {
            addSceneBubble(chatArea, text);
            lastSpeaker = null;
            continue;
        }

        if (!speakerSide[speaker]) {
            speakerSide[speaker] = sideNext;
            sideNext = (sideNext === 'left') ? 'right' : 'left';
        }

        const side = speakerSide[speaker];
        const showAvatar = lastSpeaker !== speaker;
        addChatBubble(chatArea, speaker, text, side, showAvatar);
        lastSpeaker = speaker;
    }

    if (wasAtBottom) scrollToBottom(chatArea);
}

function addChatBubble(container, speaker, text, side, showAvatar) {
    const group = document.createElement('div');
    group.className = `msg-group ${side}`;

    if (showAvatar) {
        const header = document.createElement('div');
        header.className = 'msg-header';

        const avatar = document.createElement('div');
        avatar.className = 'msg-avatar';
        avatar.style.background = getSpeakerColor(speaker);
        avatar.textContent = getAvatarChar(speaker);

        const name = document.createElement('span');
        name.className = 'msg-speaker';
        name.textContent = speaker;

        if (side === 'left') {
            header.appendChild(avatar);
            header.appendChild(name);
        } else {
            header.appendChild(name);
            header.appendChild(avatar);
        }

        group.appendChild(header);
    }

    const bubble = document.createElement('div');
    bubble.className = 'bubble';
    bubble.textContent = text;
    group.appendChild(bubble);

    container.appendChild(group);
}

function addSceneBubble(container, text) {
    const group = document.createElement('div');
    group.className = 'msg-group scene';

    const bubble = document.createElement('div');
    bubble.className = 'bubble';
    bubble.textContent = text;
    group.appendChild(bubble);

    container.appendChild(group);
}

function showPlaceholder(container) {
    container.innerHTML = '<div class="placeholder">暂无精炼内容</div>';
}

// ===== Render Story Progress =====
function renderStoryProgress(md) {
    const pane = document.getElementById('pane-story');
    pane.innerHTML = renderMarkdownHtml(md) || '<div class="placeholder">暂无故事进展</div>';
}

// ===== Render Tasks (split into active/completed) =====
function renderTasks(md) {
    const pane = document.getElementById('pane-tasks');

    // Parse tasks.md sections
    const activeMatch = md.match(/##\s*进行中\s*\n([\s\S]*?)(?=\n##\s*已完成|$)/);
    const completedMatch = md.match(/##\s*已完成\s*\n([\s\S]*)/);

    const activeRaw = activeMatch ? activeMatch[1].trim() : '';
    const completedRaw = completedMatch ? completedMatch[1].trim() : '';

    const activeHtml = activeRaw ? marked.parse(activeRaw) : '<p class="placeholder-text">暂无</p>';
    const completedHtml = completedRaw ? marked.parse(completedRaw) : '<p class="placeholder-text">暂无</p>';

    // Remove the main title
    const titleMatch = md.match(/^#\s*(.+)$/m);
    const title = titleMatch ? titleMatch[1] : '任务';

    pane.innerHTML = `
        <h1>${escapeHtml(title)}</h1>
        <div class="tasks-split">
            <div class="tasks-column">
                <h2>进行中</h2>
                <div class="md-content">${activeHtml}</div>
            </div>
            <div class="tasks-column">
                <h2>已完成</h2>
                <div class="md-content">${completedHtml}</div>
            </div>
        </div>
    `;
}

// ===== Markdown Helpers =====
marked.setOptions({ breaks: true, gfm: true });

function renderMarkdownHtml(md) {
    if (!md || md.trim() === '' || /^#.+\n\n暂无内容/.test(md)) return null;
    return `<div class="md-content">${marked.parse(md)}</div>`;
}

// ===== Tab Switching =====
document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');

        document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));
        document.getElementById('pane-' + tab.dataset.tab).classList.add('active');
    });
});

// ===== Divider Drag =====
(function() {
    const divider = document.getElementById('divider');
    const leftPanel = document.getElementById('panel-left');
    let isDragging = false;

    divider.addEventListener('mousedown', (e) => {
        isDragging = true;
        document.body.style.cursor = 'col-resize';
        document.body.style.userSelect = 'none';
        e.preventDefault();
    });

    document.addEventListener('mousemove', (e) => {
        if (!isDragging) return;
        const containerWidth = document.querySelector('.container').offsetWidth;
        const percentage = (e.clientX / containerWidth) * 100;
        leftPanel.style.width = Math.max(15, Math.min(60, percentage)) + '%';
    });

    document.addEventListener('mouseup', () => {
        if (!isDragging) return;
        isDragging = false;
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
    });
})();

// ===== Scroll Helpers =====
function isAtBottom(el) {
    return el.scrollHeight - el.scrollTop - el.clientHeight < 60;
}

function scrollToBottom(el) {
    el.scrollTop = el.scrollHeight;
}

// ===== Speaker Helpers =====
function getSpeakerColor(speaker) {
    if (!speakerColors[speaker]) {
        speakerColors[speaker] = speechColors[hashString(speaker) % speechColors.length];
    }
    return speakerColors[speaker];
}

function getAvatarChar(speaker) {
    // Take first meaningful character
    const cleaned = speaker.replace(/[\[\]]/g, '');
    return cleaned.charAt(0) || '?';
}

function hashString(str) {
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
        hash = ((hash << 5) - hash + str.charCodeAt(i)) | 0;
    }
    return Math.abs(hash);
}

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}
