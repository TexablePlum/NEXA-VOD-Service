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

async function init(config) {
    // Create a Player instance.
    const video = document.getElementById('video');
    const player = new shaka.Player();
    await player.attach(video);

    // Attach player to the window to make it easy to access in the JS console.
    window.player = player;

    // Listen for error events.
    player.addEventListener('error', onErrorEvent);

    // Configure Clear Key DRM
    // config.clearKeys is a map of keyId (hex) -> key (hex)
    if (config.clearKeys) {
        console.log('Configuring ClearKeys:', JSON.stringify(config.clearKeys));
        player.configure({
            drm: {
                clearKeys: config.clearKeys
            },
            abr: {
                enabled: false // Disable ABR for debugging
            }
        });
    } else {
        // Even if no keys, disable ABR for debugging
        player.configure({
            abr: {
                enabled: false
            }
        });
    }

    // Configure Authorization header
    if (config.accessToken) {
        console.log('Access Token provided, configuring request filter...');
        player.getNetworkingEngine().registerRequestFilter(function (type, request) {
            // Only add header for URIs that are not license requests (license requests might need different auth or none if handled by cookies/other means, but here we likely need it for content)
            // Actually, license requests in this system are handled via C# proxy, so Shaka only requests manifest and segments.
            // We should add it to all requests to the content server.
            console.log('Requesting: ' + request.uris[0]);
            request.headers['Authorization'] = 'Bearer ' + config.accessToken;
        });
    } else {
        console.warn('No Access Token provided in config!');
        showError('No Access Token provided!');
    }

    // Try to load a manifest.
    // This is an asynchronous process.
    try {
        console.log('Loading manifest: ' + config.manifestUrl);
        await player.load(config.manifestUrl);
        // This runs if the asynchronous load is successful.
        console.log('The video has now been loaded!');
    } catch (e) {
        // onError is executed if the asynchronous load fails.
        console.error('Manifest load failed:', e);
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

    video.style.display = 'none';
    errorDisplay.style.display = 'block';
    errorDisplay.textContent = message;
}
