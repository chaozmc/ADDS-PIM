// WebAuthn ceremonies for FIDO2. Options JSON comes straight from the Fido2NetLib server
// options objects (already base64url-encoded per the WebAuthn JSON spec); this module only converts
// between base64url strings and the ArrayBuffers the navigator.credentials API requires, and shapes the
// browser's result back into the exact JSON contract Fido2NetLib's AuthenticatorAttestation/AssertionRawResponse
// types expect (see Fido2WebAuthnCeremony.cs).
window.pimFido2 = window.pimFido2 || {
    async register(createOptionsJson) {
        const options = JSON.parse(createOptionsJson);
        options.challenge = base64UrlToBuffer(options.challenge);
        options.user.id = base64UrlToBuffer(options.user.id);
        if (options.excludeCredentials) {
            options.excludeCredentials = options.excludeCredentials.map(c => ({ ...c, id: base64UrlToBuffer(c.id) }));
        }

        const credential = await navigator.credentials.create({ publicKey: options });
        return JSON.stringify({
            id: credential.id,
            rawId: bufferToBase64Url(credential.rawId),
            type: credential.type,
            response: {
                attestationObject: bufferToBase64Url(credential.response.attestationObject),
                clientDataJSON: bufferToBase64Url(credential.response.clientDataJSON)
            },
            clientExtensionResults: credential.getClientExtensionResults()
        });
    },

    async authenticate(assertionOptionsJson) {
        const options = JSON.parse(assertionOptionsJson);
        options.challenge = base64UrlToBuffer(options.challenge);
        if (options.allowCredentials) {
            options.allowCredentials = options.allowCredentials.map(c => ({ ...c, id: base64UrlToBuffer(c.id) }));
        }

        const credential = await navigator.credentials.get({ publicKey: options });
        return JSON.stringify({
            id: credential.id,
            rawId: bufferToBase64Url(credential.rawId),
            type: credential.type,
            response: {
                authenticatorData: bufferToBase64Url(credential.response.authenticatorData),
                signature: bufferToBase64Url(credential.response.signature),
                clientDataJSON: bufferToBase64Url(credential.response.clientDataJSON),
                userHandle: credential.response.userHandle ? bufferToBase64Url(credential.response.userHandle) : null
            },
            clientExtensionResults: credential.getClientExtensionResults()
        });
    }
};

function base64UrlToBuffer(value) {
    const padded = value.replace(/-/g, '+').replace(/_/g, '/').padEnd(value.length + (4 - (value.length % 4)) % 4, '=');
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes.buffer;
}

function bufferToBase64Url(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
