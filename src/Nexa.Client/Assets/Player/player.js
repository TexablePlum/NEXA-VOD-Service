document.addEventListener('DOMContentLoaded', initDOM);

function initDOM() {
    console.log("DOM loaded, sending playerReady...");

    // Listen for config from C#
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', (event) => {
            const message = event.data;
            if (message && message.type === 'initConfig') {
                console.log("Received config from C#, initializing...");
                initPlayer(message.config);
            }
        });

        // Notify C# we are ready
        window.chrome.webview.postMessage("playerReady");
    } else {
        console.error("Not running in WebView2!");
        showError("Internal Error: Not running in WebView2 context");
    }
}

async function initPlayer(config) {
    console.log("Initializing player with config:", config);

    // Install built-in polyfills to patch browser incompatibilities.
    shaka.polyfill.installAll();

    if (shaka.Player.isBrowserSupported()) {
        await initShaka(config);
    } else {
        console.error('Browser not supported!');
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage("error:Browser not supported");
        }
    }
}

async function destroyPlayer() {
    if (window.player) {
        await window.player.destroy();
        window.player = null;
        console.log('Player destroyed');
    }
}

async function initShaka(config) {
    console.log('initShaka: Starting initialization...');

    try {
        // Get video element and container
        const video = document.getElementById('video');
        const videoContainer = document.querySelector('[data-shaka-player-container]');

        if (!video) {
            throw new Error('Video element not found');
        }
        if (!videoContainer) {
            throw new Error('Video container not found');
        }

        console.log('initShaka: Creating Shaka Player instance...');

        // Create player instance explicitly
        const player = new shaka.Player();
        await player.attach(video);

        console.log('initShaka: Creating Shaka UI overlay...');

        // Create UI overlay explicitly (fixes race condition)
        const ui = new shaka.ui.Overlay(player, videoContainer, video);
        const controls = ui.getControls();

        // Attach to window for debugging
        window.player = player;
        window.ui = ui;
        window.controls = controls;

        console.log('initShaka: Shaka Player and UI initialized successfully');

        // Initialization Watchdog
        // If playback doesn't start within 15 seconds, report error
        const watchdog = setTimeout(() => {
            if (video.paused && video.currentTime === 0) {
                console.error('Initialization timed out');
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage("error:Player initialization timed out. Check network connection.");
                }
            }
        }, 15000);

        // Handle poster
        if (config.posterUrl) {
            video.poster = config.posterUrl;
            console.log('initShaka: Poster set:', config.posterUrl);
        } else {
            video.removeAttribute('poster');
        }

        // Listen for playback start
        video.addEventListener('playing', () => {
            clearTimeout(watchdog); // Cancel watchdog
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

        console.log('initShaka: Configuring player...');

        // Configure DRM
        player.configure({
            drm: {
                clearKeys: config.clearKeys
            },
            // Enable ABR with aggressive settings for premium feel
            abr: {
                enabled: true,
                defaultBandwidthEstimate: 10_000_000, // Start high (10 Mbps)
                switchInterval: 0, // Switch immediately when bandwidth allows
                bandwidthUpgradeTarget: 0.8, // Upgrade when 80% of bandwidth is enough
                restrictions: {
                    maxHeight: config.maxHeight || 4320 // Cap resolution based on license
                }
            },
            streaming: {
                bufferingGoal: 12, // Buffer more content (12s)
                rebufferingGoal: 2 // Start playing after 2s buffered
            }
        });

        console.log('initShaka: Adding request filter for Authorization...');

        // Add Authorization header
        player.getNetworkingEngine().registerRequestFilter((type, request) => {
            if (type === shaka.net.NetworkingEngine.RequestType.SEGMENT ||
                type === shaka.net.NetworkingEngine.RequestType.MANIFEST) {
                if (config.accessToken) {
                    request.headers['Authorization'] = `Bearer ${config.accessToken}`;
                }
            }
        });

        console.log('initShaka: Loading manifest:', config.manifestUrl);

        // Load manifest
        await player.load(config.manifestUrl);

        console.log('initShaka: Manifest loaded successfully!');
        console.log('initShaka: Max Height restricted to:', config.maxHeight);

    } catch (error) {
        console.error('initShaka: Failed to initialize player:', error);
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(`error:Failed to initialize player: ${error.message}`);
        }
        showError(`Initialization Error: ${error.message}`);
    }
}

function onErrorEvent(event) {
    // Extract the shaka.util.Error object from the event.
    onError(event.detail);
}

function onError(error) {
    // Log the error.
    console.error('Error code', error.code, 'object', error);
    // Send error to C# host
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(`error:Player Error ${error.code}: ${error.message}`);
    }
    showError(`Error code: ${error.code} - ${error.message}`);
}

function showError(message) {
    const errorDisplay = document.getElementById('error-display');
    const video = document.getElementById('video');

    // Hide video container if possible, or just overlay error
    errorDisplay.style.display = 'block';
    errorDisplay.textContent = message;
}
