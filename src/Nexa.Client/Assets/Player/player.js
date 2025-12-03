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

        // Configure UI to remove Picture-in-Picture and fix layout
        const uiConfig = {
            'controlPanelElements': ['play_pause', 'mute', 'volume', 'time_and_duration', 'spacer', 'overflow_menu', 'fullscreen'],
            'overflowMenuButtons': ['captions', 'quality', 'language', 'playback_rate'] // Removed 'picture_in_picture' and 'cast'
        };
        ui.configure(uiConfig);

        // Disable PiP on video element as well
        video.disablePictureInPicture = true;

        const controls = ui.getControls();

        // Attach to window for debugging
        window.player = player;
        window.ui = ui;
        window.controls = controls;

        console.log('initShaka: Shaka Player and UI initialized successfully');

        // Setup global keyboard shortcuts
        setupKeyboardShortcuts(player, video, ui);

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

function setupKeyboardShortcuts(player, video, ui) {
    console.log('Setting up global keyboard shortcuts (only for non-fullscreen mode)...');

    // Track if user is typing in an input field (shouldn't happen in player, but good practice)
    const isTyping = () => {
        const activeElement = document.activeElement;
        return activeElement && (
            activeElement.tagName === 'INPUT' ||
            activeElement.tagName === 'TEXTAREA' ||
            activeElement.isContentEditable
        );
    };

    document.addEventListener('keydown', (event) => {
        // Don't handle shortcuts if user is typing
        if (isTyping()) return;

        // IMPORTANT: Only handle keyboard shortcuts when NOT in fullscreen
        // In fullscreen, let Shaka Player's built-in keyboard handling work
        if (document.fullscreenElement) {
            return; // Exit early - we're in fullscreen, let Shaka handle it
        }

        const key = event.key.toLowerCase();

        // Prevent default behavior for handled keys (only when not in fullscreen)
        const handledKeys = [' ', 'arrowleft', 'arrowright', 'arrowup', 'arrowdown', 'f', 'm'];
        if (handledKeys.includes(key)) {
            event.preventDefault();
        }

        switch (key) {
            case ' ': // Space - Play/Pause
                if (video.paused) {
                    video.play();
                } else {
                    video.pause();
                }
                break;

            case 'arrowleft': // Left Arrow - Rewind 10 seconds
                video.currentTime = Math.max(0, video.currentTime - 10);
                break;

            case 'arrowright': // Right Arrow - Forward 10 seconds
                video.currentTime = Math.min(video.duration, video.currentTime + 10);
                break;

            case 'arrowup': // Up Arrow - Volume up
                video.volume = Math.min(1, video.volume + 0.1);
                break;

            case 'arrowdown': // Down Arrow - Volume down
                video.volume = Math.max(0, video.volume - 0.1);
                break;

            case 'f': // F - Toggle fullscreen
                toggleFullscreen(video);
                break;

            case 'm': // M - Toggle mute
                video.muted = !video.muted;
                break;
        }
    });

    // Separate listener for number keys (0-9) - works in BOTH fullscreen and non-fullscreen
    // This is a custom feature not provided by Shaka Player, so it's safe to always handle
    document.addEventListener('keydown', (event) => {
        if (isTyping()) return;

        const key = event.key;

        // Handle number keys 0-9
        if (key >= '0' && key <= '9') {
            event.preventDefault();
            const percentage = parseInt(key) / 10;
            if (!isNaN(percentage) && video.duration) {
                video.currentTime = video.duration * percentage;
            }
        }
    });

    console.log('Global keyboard shortcuts registered (active only when not in fullscreen)');
}

function toggleFullscreen(video) {
    const container = document.querySelector('[data-shaka-player-container]');

    if (!document.fullscreenElement) {
        // Enter fullscreen
        if (container.requestFullscreen) {
            container.requestFullscreen();
        } else if (container.webkitRequestFullscreen) {
            container.webkitRequestFullscreen();
        } else if (container.msRequestFullscreen) {
            container.msRequestFullscreen();
        }
    } else {
        // Exit fullscreen
        if (document.exitFullscreen) {
            document.exitFullscreen();
        } else if (document.webkitExitFullscreen) {
            document.webkitExitFullscreen();
        } else if (document.msExitFullscreen) {
            document.msExitFullscreen();
        }
    }
}

function showError(message) {

    const errorDisplay = document.getElementById('error-display');
    const video = document.getElementById('video');

    // Hide video container if possible, or just overlay error
    errorDisplay.style.display = 'block';
    errorDisplay.textContent = message;
}
