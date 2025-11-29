async function initPlayer(config) {
    // Install built-in polyfills to patch browser incompatibilities.
    shaka.polyfill.installAll();

    // Check to see if the browser supports the basic APIs Shaka needs.
    console.log('isSecureContext:', window.isSecureContext);

    if (shaka.Player.isBrowserSupported()) {
        // Everything looks good!
        await init(config);
    } else {
        // This browser does not have the minimum set of APIs we need.
        console.error('Browser not supported!');
        showError('Browser not supported!');
    }
}

async function destroyPlayer() {
    if (window.player) {
        await window.player.destroy();
        window.player = null;
        console.log('Player destroyed');
    }
}

async function init(config) {
    // When using Shaka UI, we don't create player manually, we use the UI library.
    const video = document.getElementById('video');
    const ui = video['ui'];
    const controls = ui.getControls();
    const player = controls.getPlayer();

    // Attach player to window for debugging
    window.player = player;
    window.ui = ui;

    // Handle poster
    if (config.posterUrl) {
        video.poster = config.posterUrl;
    } else {
        video.removeAttribute('poster');
    }

    // Listen for playback start
    video.addEventListener('playing', () => {
        console.log('Video playing, notifying host...');
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage("playbackStarted");
        }
    });

    // Track user activity to show/hide controls (Simple & Reliable)
    // This syncs with Shaka UI's internal mouse movement detection
    let lastMouseMoveTime = 0;
    document.addEventListener('mousemove', () => {
        // Throttle messages
        const now = Date.now();
        if (now - lastMouseMoveTime > 200) {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage("userActive");
            }
            lastMouseMoveTime = now;
        }
    });

    // Listen for error events.
    player.addEventListener('error', onErrorEvent);

    // Configure DRM
    player.configure({
        drm: {
            clearKeys: config.clearKeys
        },
        // Enable ABR with aggressive settings for premium feel
        abr: {
            enabled: true,
            defaultBandwidthEstimate: 10_000_000, // Start high (25 Mbps)
            switchInterval: 0, // Switch immediately when bandwidth allows
            bandwidthUpgradeTarget: 0.8 // Upgrade when 80% of bandwidth is enough
        },
        streaming: {
            bufferingGoal: 12, // Buffer more content (30s)
            rebufferingGoal: 2 // Start playing after 2s buffered
        }
    });

    // Add Authorization header
    player.getNetworkingEngine().registerRequestFilter((type, request) => {
        if (type === shaka.net.NetworkingEngine.RequestType.SEGMENT ||
            type === shaka.net.NetworkingEngine.RequestType.MANIFEST) {
            if (config.accessToken) {
                request.headers['Authorization'] = `Bearer ${config.accessToken}`;
            }
        }
    });

    // Load manifest
    try {
        await player.load(config.manifestUrl);
        console.log('The video has now been loaded!');
    } catch (e) {
        onError(e);
    }
}

function onErrorEvent(event) {
    // Extract the shaka.util.Error object from the event.
    onError(event.detail);
}

function onError(error) {
    // Log the error.
    console.error('Error code', error.code, 'object', error);
    showError(`Error code: ${error.code} - ${error.message}`);
}

function showError(message) {
    const errorDisplay = document.getElementById('error-display');
    const video = document.getElementById('video');

    // Hide video container if possible, or just overlay error
    errorDisplay.style.display = 'block';
    errorDisplay.textContent = message;
}
