import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import createPageController, {
    buildConfigurationCandidate,
    createAnnouncementGate,
    formatOperationalStatus,
    mergeCategoryChoices,
    recoveryCommand,
    validateEditor
} from '../../Jellyfin.Plugin.QControl/Configuration/configPage.js';

class FakeClassList {
    values = new Set();

    add(...values) {
        values.forEach(value => this.values.add(value));
    }

    remove(...values) {
        values.forEach(value => this.values.delete(value));
    }
}

class FakeElement {
    constructor({ action = null } = {}) {
        this.value = '';
        this.checked = false;
        this.hidden = false;
        this.disabled = false;
        this.textContent = '';
        this.innerHTML = '';
        this.placeholder = '';
        this.open = false;
        this.focused = false;
        this.dataset = action ? { action } : {};
        this.classList = new FakeClassList();
        this.listeners = new Map();
        this.attributes = new Map();
    }

    addEventListener(type, listener) {
        this.listeners.set(type, listener);
    }

    async dispatch(type, details = {}) {
        const listener = this.listeners.get(type);
        assert.ok(listener, `Expected a ${type} listener.`);
        await listener({ target: this, preventDefault() {}, ...details });
    }

    closest(selector) {
        return selector === 'button[data-action]' && this.dataset.action ? this : null;
    }

    setAttribute(name, value) {
        this.attributes.set(name, value);
    }

    removeAttribute(name) {
        this.attributes.delete(name);
    }

    showModal() {
        this.open = true;
    }

    close() {
        this.open = false;
    }

    focus() {
        this.focused = true;
    }
}

class FakeView {
    constructor() {
        this.listeners = new Map();
        this.elements = new Map();
        this.categoryChoices = [];
        for (const action of [
            'refresh-status',
            'test-connection',
            'save-configuration',
            'resume-marked',
            'restore-speed',
            'mark-resolved',
            'confirm-recovery',
            'cancel-recovery'
        ]) {
            this.elements.set(`[data-action="${action}"]`, new FakeElement({ action }));
        }
    }

    addEventListener(type, listener) {
        this.listeners.set(type, listener);
    }

    querySelector(selector) {
        if (!this.elements.has(selector)) {
            this.elements.set(selector, new FakeElement());
        }

        return this.elements.get(selector);
    }

    querySelectorAll(selector) {
        return selector === '[data-category-choice]:checked'
            ? this.categoryChoices.filter(choice => choice.checked)
            : [];
    }

    contains() {
        return true;
    }

    async dispatch(type, target = this) {
        const listener = this.listeners.get(type);
        assert.ok(listener, `Expected a ${type} listener.`);
        await listener({ target, preventDefault() {} });
    }
}

function configuration(overrides = {}) {
    return {
        Revision: 4,
        QbittorrentBaseAddress: 'http://qbittorrent:8080',
        CredentialMode: 'StoredApiKey',
        HasStoredApiKey: true,
        SecretFilePath: '',
        ConnectionValidated: true,
        AlternativeLimitsEnabled: true,
        StopTorrentsEnabled: false,
        StopScope: 'All',
        SelectedCategories: [],
        IncludeIncomplete: true,
        IncludeCompleted: true,
        MarkerTag: 'jfStopped',
        NeverTouchTag: 'jfNeverTouch',
        ReleaseGraceSeconds: 60,
        ...overrides
    };
}

function operationalStatus(overrides = {}) {
    return {
        Connectivity: 'Connected',
        ApplicationVersion: '5.2.3',
        WebApiVersion: '2.15.1',
        ProtectionState: 'Inactive',
        QualifyingSessionCount: 0,
        AlternativeLimitsActionEnabled: true,
        StopTorrentsActionEnabled: false,
        ConfigurationChangesPending: false,
        CanResumeMarkedTorrents: false,
        CanRestorePreviousSpeedSetting: false,
        CanMarkResolved: false,
        ...overrides
    };
}

function createHarness(responder = async (path, method) => {
    if (path === 'QControl/Configuration' && method === 'GET') {
        return configuration();
    }

    if (path === 'QControl/Status') {
        return operationalStatus();
    }

    if (path === 'QControl/Connection/Categories') {
        return { IsConnected: true, Categories: ['radarr', 'sonarr'] };
    }

    return {};
}) {
    const calls = [];
    const scheduled = [];
    const view = new FakeView();
    const apiClient = {
        getUrl: path => path,
        ajax: async options => {
            const body = options.data ? JSON.parse(options.data) : undefined;
            calls.push({ method: options.type, path: options.url, body });
            return responder(options.url, options.type, body);
        }
    };
    globalThis.window = {
        ApiClient: apiClient,
        Dashboard: { showLoadingMsg() {}, hideLoadingMsg() {} },
        setTimeout(action) {
            scheduled.push(action);
            return scheduled.length;
        },
        clearTimeout() {}
    };
    createPageController(view);
    return { calls, scheduled, view };
}

test('category choices retain configured values that discovery no longer returns', () => {
    assert.deepEqual(
        mergeCategoryChoices(['sonarr', 'radarr'], ['radarr', 'missing']),
        [
            { value: 'missing', missing: true },
            { value: 'radarr', missing: false },
            { value: 'sonarr', missing: false }
        ]);
});

test('candidate carries only write-only credential controls and supports source switching', () => {
    const candidate = buildConfigurationCandidate({
        revision: 7,
        baseAddress: ' http://qbittorrent:8080/ ',
        credentialMode: 'SecretFile',
        apiKeyReplacement: '',
        clearStoredApiKey: true,
        secretFilePath: ' C:\\ProgramData\\Jellyfin\\qbit-api-key.txt ',
        alternativeLimitsEnabled: true,
        stopTorrentsEnabled: true,
        stopScope: 'SelectedCategories',
        selectedCategories: ['sonarr'],
        includeIncomplete: true,
        includeCompleted: false,
        markerTag: 'jfStopped',
        neverTouchTag: 'jfNeverTouch',
        releaseGraceSeconds: '60'
    });

    assert.equal(candidate.ExpectedRevision, 7);
    assert.equal(candidate.CredentialMode, 1);
    assert.equal(candidate.ApiKeyReplacement, '');
    assert.equal(candidate.ClearStoredApiKey, true);
    assert.equal(candidate.StopScope, 1);
    assert.deepEqual(candidate.SelectedCategories, ['sonarr']);
    assert.equal('QbittorrentApiKey' in candidate, false);
    assert.doesNotMatch(JSON.stringify(candidate), /qbt_/i);
});

test('editor shape blocks an unusable stop configuration without replacing server validation', () => {
    const base = {
        stopTorrentsEnabled: true,
        includeIncomplete: false,
        includeCompleted: false,
        markerTag: 'jfStopped',
        neverTouchTag: 'jfNeverTouch',
        stopScope: 0,
        selectedCategories: []
    };

    assert.match(validateEditor(base), /completed or incomplete/i);
    assert.match(validateEditor({ ...base, includeIncomplete: true, markerTag: 'same', neverTouchTag: 'same' }), /different/i);
    assert.equal(validateEditor({ ...base, includeIncomplete: true }), null);
});

test('status summary presents server state and no media or torrent display data', () => {
    const summary = formatOperationalStatus({
        Connectivity: 'Connected',
        ApplicationVersion: '5.2.3',
        WebApiVersion: '2.15.1',
        ProtectionState: 'ReleasePending',
        QualifyingSessionCount: 0,
        AlternativeLimitsActionEnabled: true,
        StopTorrentsActionEnabled: true,
        AlternativeLimitsCurrentlyEnabled: true,
        AlternativeLimitsOwned: true,
        EligibleTorrentCount: 2,
        MarkedTorrentCount: 3,
        StoppedMarkedTorrentCount: 2,
        ExcludedTorrentCount: 1,
        ReleaseGraceRemainingSeconds: 17,
        ConfigurationChangesPending: true,
        CurrentError: null,
        UserName: 'private-user',
        MediaTitle: 'private-title',
        TorrentName: 'private-torrent'
    });

    assert.match(summary.headline, /release in 17 seconds/i);
    assert.match(summary.details.join(' '), /qBittorrent 5\.2\.3/i);
    assert.match(summary.details.join(' '), /2 stopped and marked/i);
    assert.doesNotMatch(JSON.stringify(summary), /private-/i);
});

test('announcement gate emits only meaningful status changes', () => {
    const messages = [];
    const announce = createAnnouncementGate(message => messages.push(message));

    announce('Protection active.');
    announce('Protection active.');
    announce('Release in 12 seconds.');

    assert.deepEqual(messages, ['Protection active.', 'Release in 12 seconds.']);
});

test('recovery commands explain effects before their administrator-only requests', () => {
    assert.deepEqual(recoveryCommand('resume-marked'), {
        endpoint: 'QControl/Recovery/ResumeMarkedTorrents',
        title: 'Resume marked torrents?',
        description: 'Start non-excluded torrents carrying the configured Marker Tag, then remove that tag after qBittorrent accepts the starts.'
    });
    assert.match(recoveryCommand('mark-resolved').description, /does not change qBittorrent/i);
});

test('administrator page remains a native responsive Jellyfin form', async () => {
    const html = await readFile(new URL(
        '../../Jellyfin.Plugin.QControl/Configuration/configPage.html',
        import.meta.url), 'utf8');

    const actionTags = [...html.matchAll(/<(\w+)[^>]*data-action=/g)].map(match => match[1]);
    assert.ok(actionTags.length >= 6);
    assert.ok(actionTags.every(tag => tag === 'button'));
    assert.match(html, /aria-live="polite"/);
    assert.match(html, /<dialog[^>]+id="qControlRecoveryDialog"/);
    assert.match(html, /<form[^>]+inert[^>]+aria-busy="true"/);
    assert.match(html, /@media \(max-width: 52rem\)/);
});

test('controller entry point is exported for Jellyfin page lifecycle binding', () => {
    assert.equal(typeof createPageController, 'function');
});

test('loading a stored credential exposes presence but never its content', async () => {
    const { calls, view } = createHarness();

    await view.dispatch('viewshow');

    const apiKey = view.querySelector('#qControlApiKey');
    assert.equal(view.querySelector('#qControlConfigurationForm').attributes.get('aria-busy'), 'false');
    assert.equal(view.querySelector('#qControlConfigurationForm').attributes.has('inert'), false);
    assert.equal(apiKey.value, '');
    assert.match(apiKey.placeholder, /configured/i);
    assert.doesNotMatch(JSON.stringify(calls), /qbt_/i);
});

test('connection test sends a write-only replacement while switching to a native secret file', async () => {
    const { calls, view } = createHarness(async (path, method) => {
        if (path === 'QControl/Configuration' && method === 'GET') {
            return configuration();
        }

        if (path === 'QControl/Status') {
            return operationalStatus();
        }

        if (path === 'QControl/Connection/Categories') {
            return { IsConnected: true, Categories: [] };
        }

        if (path === 'QControl/Connection/Test') {
            return {
                IsConnected: true,
                ApplicationVersion: '5.2.3',
                WebApiVersion: '2.15.1',
                Categories: ['radarr']
            };
        }

        return {};
    });
    await view.dispatch('viewshow');
    view.querySelector('#qControlCredentialMode').value = '1';
    view.querySelector('#qControlSecretFilePath').value = 'C:\\ProgramData\\Jellyfin\\qbit.key';
    await view.dispatch('change', view.querySelector('#qControlCredentialMode'));

    assert.equal(view.querySelector('#qControlStoredCredential').hidden, true);
    assert.equal(view.querySelector('#qControlSecretCredential').hidden, false);

    await view.dispatch('click', view.querySelector('[data-action="test-connection"]'));

    const request = calls.find(call => call.path === 'QControl/Connection/Test');
    assert.equal(request.body.CredentialMode, 1);
    assert.equal(request.body.SecretFilePath, 'C:\\ProgramData\\Jellyfin\\qbit.key');
    assert.equal(request.body.ApiKeyReplacement, '');
    assert.equal('QbittorrentApiKey' in request.body, false);
});

test('administrator can test an explicit unauthenticated connection without credential inputs', async () => {
    const html = await readFile(new URL(
        '../../Jellyfin.Plugin.QControl/Configuration/configPage.html',
        import.meta.url), 'utf8');
    const { calls, view } = createHarness(async (path, method) => {
        if (path === 'QControl/Configuration' && method === 'GET') {
            return configuration({ CredentialMode: 'Unauthenticated' });
        }

        if (path === 'QControl/Status') {
            return operationalStatus();
        }

        if (path === 'QControl/Connection/Categories') {
            return { IsConnected: true, Categories: [] };
        }

        if (path === 'QControl/Connection/Test') {
            return { IsConnected: true, Categories: [] };
        }

        return {};
    });

    await view.dispatch('viewshow');

    assert.match(html, /<option value="2">No authentication/);
    assert.equal(view.querySelector('#qControlCredentialMode').value, '2');
    assert.equal(view.querySelector('#qControlStoredCredential').hidden, true);
    assert.equal(view.querySelector('#qControlSecretCredential').hidden, true);

    await view.dispatch('click', view.querySelector('[data-action="test-connection"]'));

    const request = calls.find(call => call.path === 'QControl/Connection/Test');
    assert.equal(request.body.CredentialMode, 2);
    assert.equal(request.body.ApiKeyReplacement, '');
    assert.equal(request.body.SecretFilePath, '');
});

test('recovery requires the native confirmation dialog before the server command', async () => {
    const { calls, view } = createHarness(async (path, method) => {
        if (path === 'QControl/Configuration' && method === 'GET') {
            return configuration();
        }

        if (path === 'QControl/Status') {
            return operationalStatus({ CanResumeMarkedTorrents: true });
        }

        if (path === 'QControl/Connection/Categories') {
            return { IsConnected: true, Categories: [] };
        }

        if (path === 'QControl/Recovery/ResumeMarkedTorrents') {
            return { Outcome: 'Completed' };
        }

        return {};
    });
    await view.dispatch('viewshow');
    const opener = view.querySelector('[data-action="resume-marked"]');

    await view.dispatch('click', opener);

    assert.equal(view.querySelector('#qControlRecoveryDialog').open, true);
    assert.match(view.querySelector('#qControlRecoveryDialogDescription').textContent, /start non-excluded/i);
    assert.equal(calls.some(call => call.path.includes('/Recovery/')), false);

    await view.dispatch('click', view.querySelector('[data-action="confirm-recovery"]'));

    assert.equal(calls.filter(call => call.path === 'QControl/Recovery/ResumeMarkedTorrents').length, 1);
    assert.equal(view.querySelector('#qControlRecoveryDialog').open, false);
    assert.equal(opener.focused, true);
});

test('server topology conflict is rendered without client policy substitution', async () => {
    const { view } = createHarness(async (path, method) => {
        if (path === 'QControl/Configuration' && method === 'GET') {
            return configuration();
        }

        if (path === 'QControl/Status') {
            return operationalStatus();
        }

        if (path === 'QControl/Connection/Categories') {
            return { IsConnected: true, Categories: [] };
        }

        if (path === 'QControl/Configuration' && method === 'PUT') {
            throw { json: async () => ({ Outcome: 'ActiveConnectionConflict' }) };
        }

        return {};
    });
    await view.dispatch('viewshow');

    await view.dispatch('click', view.querySelector('[data-action="save-configuration"]'));

    assert.match(
        view.querySelector('#qControlConfigurationStatus').textContent,
        /cannot change during an active protection session/i);
});
