const statusRefreshMilliseconds = 5000;

function property(value, name, fallback = undefined) {
    if (value && value[name] !== undefined && value[name] !== null) {
        return value[name];
    }

    const camelName = name.charAt(0).toLowerCase() + name.slice(1);
    return value && value[camelName] !== undefined && value[camelName] !== null
        ? value[camelName]
        : fallback;
}

function escapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}

function enumValue(value, choices, fallback) {
    if (typeof value === 'number' && Number.isInteger(value)) {
        return value;
    }

    const text = String(value ?? '');
    if (/^\d+$/.test(text)) {
        return Number(text);
    }

    const index = choices.findIndex(choice => choice.toLowerCase() === text.toLowerCase());
    return index >= 0 ? index : fallback;
}

/**
 * Merges discovered qBittorrent categories with configured values that may be temporarily absent.
 *
 * @param {Array<string>} discovered categories returned by qBittorrent
 * @param {Array<string>} configured exact configured categories
 * @returns {Array<{value: string, missing: boolean}>} stable category choices
 */
export function mergeCategoryChoices(discovered, configured) {
    const live = new Set((discovered ?? []).map(value => String(value)));
    const all = new Set([...live, ...(configured ?? []).map(value => String(value))]);
    return [...all]
        .sort((left, right) => left.localeCompare(right))
        .map(value => ({ value, missing: !live.has(value) }));
}

/**
 * Merges the global qBittorrent tag catalog with configured custom values.
 *
 * @param {Array<string>} discovered tags returned by qBittorrent
 * @param {Array<string>} configured exact configured exclusion tags
 * @returns {Array<string>} stable tag suggestions
 */
export function mergeTagSuggestions(discovered, configured) {
    return [...new Set([...(discovered ?? []), ...(configured ?? [])]
        .map(value => String(value).trim())
        .filter(Boolean))]
        .sort();
}

function normalizedExclusionTags(values) {
    return [...new Set((values ?? []).map(value => String(value).trim()))].sort();
}

function exclusionTagProblem(exclusionTags) {
    if (exclusionTags.length > 64) {
        return 'Use no more than 64 Exclusion Tags.';
    }

    if (exclusionTags.some(tag => !tag || tag.length > 128 || tag.includes(',') || /[\u0000-\u001f\u007f]/.test(tag))) {
        return 'Exclusion Tags must be 1–128 characters and cannot contain commas, newlines, or control characters.';
    }

    return null;
}

/**
 * Builds the complete server candidate while treating credential content as write-only.
 *
 * @param {object} editor current editor values
 * @returns {object} complete API candidate
 */
export function buildConfigurationCandidate(editor) {
    return {
        ExpectedRevision: Number(editor.revision ?? 0),
        QbittorrentBaseAddress: String(editor.baseAddress ?? '').trim(),
        CredentialMode: enumValue(editor.credentialMode, ['StoredApiKey', 'SecretFile', 'Unauthenticated'], 0),
        SecretFilePath: String(editor.secretFilePath ?? '').trim(),
        ApiKeyReplacement: String(editor.apiKeyReplacement ?? ''),
        ClearStoredApiKey: Boolean(editor.clearStoredApiKey),
        AlternativeLimitsEnabled: Boolean(editor.alternativeLimitsEnabled),
        StopTorrentsEnabled: Boolean(editor.stopTorrentsEnabled),
        StopScope: enumValue(editor.stopScope, ['All', 'SelectedCategories'], 0),
        SelectedCategories: [...new Set((editor.selectedCategories ?? []).map(value => String(value)))]
            .sort((left, right) => left.localeCompare(right)),
        IncludeIncomplete: Boolean(editor.includeIncomplete),
        IncludeCompleted: Boolean(editor.includeCompleted),
        MarkerTag: String(editor.markerTag ?? '').trim(),
        ExclusionTags: normalizedExclusionTags(editor.exclusionTags),
        ReleaseGraceSeconds: Number(editor.releaseGraceSeconds ?? 0)
    };
}

/**
 * Builds a connection-only update while retaining the saved protection settings.
 *
 * @param {object} configuration current credential-safe server configuration
 * @param {object} editor current connection editor values
 * @returns {object} complete API candidate
 */
export function buildConnectionCandidate(configuration, editor) {
    return buildConfigurationCandidate({
        revision: property(configuration, 'Revision', 0),
        baseAddress: editor.baseAddress,
        credentialMode: editor.credentialMode,
        apiKeyReplacement: editor.apiKeyReplacement,
        clearStoredApiKey: editor.clearStoredApiKey,
        secretFilePath: editor.secretFilePath,
        alternativeLimitsEnabled: property(configuration, 'AlternativeLimitsEnabled', false),
        stopTorrentsEnabled: property(configuration, 'StopTorrentsEnabled', false),
        stopScope: property(configuration, 'StopScope', 0),
        selectedCategories: property(configuration, 'SelectedCategories', []) ?? [],
        includeIncomplete: property(configuration, 'IncludeIncomplete', true),
        includeCompleted: property(configuration, 'IncludeCompleted', true),
        markerTag: property(configuration, 'MarkerTag', 'qcontrol-resume'),
        exclusionTags: property(configuration, 'ExclusionTags', ['qcontrol-ignore']) ?? [],
        releaseGraceSeconds: property(configuration, 'ReleaseGraceSeconds', 60)
    });
}

/**
 * Checks only whether the editor can express a usable request; the server remains authoritative.
 *
 * @param {object} editor current editor values
 * @returns {string|null} one plain-language editor problem
 */
export function validateEditor(editor) {
    const exclusionTags = normalizedExclusionTags(editor.exclusionTags);
    const tagProblem = exclusionTagProblem(exclusionTags);
    if (tagProblem) {
        return tagProblem;
    }

    if (!editor.stopTorrentsEnabled) {
        return null;
    }

    if (!editor.includeIncomplete && !editor.includeCompleted) {
        return 'Choose completed or incomplete torrents before saving Stop Torrents.';
    }

    const marker = String(editor.markerTag ?? '').trim();
    if (!marker) {
        return 'Marker Tag is required.';
    }

    if (exclusionTags.includes(marker)) {
        return 'Marker Tag and Exclusion Tags must be different.';
    }

    const scope = enumValue(editor.stopScope, ['All', 'SelectedCategories'], 0);
    if (scope === 1 && (editor.selectedCategories ?? []).length === 0) {
        return 'Choose at least one category for selected-category scope.';
    }

    return null;
}

function protectionState(value) {
    const labels = ['Inactive', 'Protecting', 'ReleasePending', 'Restoring', 'RecoveryRequired'];
    const index = enumValue(value, labels, -1);
    return index >= 0 ? labels[index] : String(value ?? 'Unknown');
}

function connectivityState(value) {
    const labels = ['Unconfigured', 'Connected', 'Failed'];
    const index = enumValue(value, labels, -1);
    return index >= 0 ? labels[index] : String(value ?? 'Unknown');
}

/**
 * Formats only the server's bounded operational fields.
 *
 * @param {object} status server operational status
 * @returns {{headline: string, details: Array<string>}} privacy-safe summary
 */
export function formatOperationalStatus(status) {
    const lifecycle = protectionState(property(status, 'ProtectionState', 'Inactive'));
    const sessions = Number(property(status, 'QualifyingSessionCount', 0));
    const grace = property(status, 'ReleaseGraceRemainingSeconds', null);
    let headline;
    switch (lifecycle) {
        case 'Protecting':
            headline = `Protection active for ${sessions} Jellyfin player${sessions === 1 ? '' : 's'}.`;
            break;
        case 'ReleasePending':
            headline = `Playback is absent; release in ${Number(grace ?? 0)} seconds.`;
            break;
        case 'Restoring':
            headline = 'QControl is restoring state owned by this activation.';
            break;
        case 'RecoveryRequired':
            headline = 'Administrator recovery is required before automatic release.';
            break;
        default:
            headline = 'QControl is inactive.';
            break;
    }

    const connectivity = connectivityState(property(status, 'Connectivity', 'Unconfigured'));
    const applicationVersion = property(status, 'ApplicationVersion', null);
    const webApiVersion = property(status, 'WebApiVersion', null);
    const details = [];
    if (connectivity === 'Connected') {
        details.push(`Connected to qBittorrent ${applicationVersion ?? 'unknown'} (Web API ${webApiVersion ?? 'unknown'}).`);
    } else if (connectivity === 'Failed') {
        details.push('qBittorrent is currently unreachable or rejected the configured authentication.');
    } else {
        details.push('No validated qBittorrent connection is active.');
    }

    const alternativeEnabled = Boolean(property(status, 'AlternativeLimitsActionEnabled', false));
    const stopEnabled = Boolean(property(status, 'StopTorrentsActionEnabled', false));
    details.push(`Actions: Alternative Limits ${alternativeEnabled ? 'enabled' : 'disabled'}; Stop Torrents ${stopEnabled ? 'enabled' : 'disabled'}.`);
    const limitsCurrent = property(status, 'AlternativeLimitsCurrentlyEnabled', null);
    if (limitsCurrent !== null) {
        const owned = Boolean(property(status, 'AlternativeLimitsOwned', false));
        details.push(`Alternative Limits is ${limitsCurrent ? 'on' : 'off'}${owned ? ' and owned by this activation' : ''}.`);
    }

    const marked = property(status, 'MarkedTorrentCount', null);
    const stoppedMarked = property(status, 'StoppedMarkedTorrentCount', null);
    const eligible = property(status, 'EligibleTorrentCount', null);
    const excluded = property(status, 'ExcludedTorrentCount', null);
    if (marked !== null) {
        details.push(`${Number(stoppedMarked ?? 0)} stopped and marked; ${Number(marked)} total marked; ${Number(eligible ?? 0)} currently eligible; ${Number(excluded ?? 0)} excluded.`);
    }

    if (Boolean(property(status, 'ConfigurationChangesPending', false))) {
        details.push('Saved behavior changes will apply to the next activation.');
    }

    const error = property(status, 'CurrentError', null);
    if (error !== null) {
        details.push(`Current bounded error: ${String(error)}.`);
    }

    return { headline, details };
}

/**
 * Deduplicates accessible announcements while allowing visual countdown refreshes.
 *
 * @param {(message: string) => void} emit live-region writer
 * @returns {(message: string) => void} gated writer
 */
export function createAnnouncementGate(emit) {
    let previous = null;
    return message => {
        const next = String(message ?? '');
        if (next === previous) {
            return;
        }

        previous = next;
        emit(next);
    };
}

/**
 * Returns the server route and effect text for one explicit recovery command.
 *
 * @param {string} action action identifier
 * @returns {{endpoint: string, title: string, description: string}} command contract
 */
export function recoveryCommand(action) {
    const commands = {
        'resume-marked': {
            endpoint: 'QControl/Recovery/ResumeMarkedTorrents',
            title: 'Resume marked torrents?',
            description: 'Start non-excluded torrents carrying the configured Marker Tag, then remove that tag after qBittorrent accepts the starts.'
        },
        'restore-speed': {
            endpoint: 'QControl/Recovery/RestorePreviousSpeedSetting',
            title: 'Restore the previous speed setting?',
            description: 'Set qBittorrent Alternative Limits to the mode recorded before the interrupted activation. Torrent states and tags are not changed.'
        },
        'mark-resolved': {
            endpoint: 'QControl/Recovery/MarkResolved',
            title: 'Mark recovery resolved?',
            description: 'Clear QControl’s recovery record. This does not change qBittorrent torrents, tags, categories, or speed settings.'
        }
    };
    const command = commands[action];
    if (!command) {
        throw new TypeError(`Unknown recovery action: ${String(action)}`);
    }

    return command;
}

async function requestJson(apiClient, method, path, body = undefined) {
    const options = {
        type: method,
        url: apiClient.getUrl(path),
        dataType: 'json'
    };
    if (body !== undefined) {
        options.contentType = 'application/json';
        options.data = JSON.stringify(body);
    }

    try {
        return await apiClient.ajax(options);
    } catch (error) {
        if (error && typeof error.json === 'function') {
            try {
                error.qControlPayload = await error.json();
            } catch {
                // Preserve the original HTTP error when no bounded JSON body exists.
            }
        }

        throw error;
    }
}

function responsePayload(error) {
    return error?.qControlPayload ?? null;
}

function outcomeEquals(value, expected) {
    return String(value ?? '').toLowerCase() === expected.toLowerCase()
        || Number(value) === ['Accepted', 'Invalid', 'RevisionConflict', 'ConnectionFailed', 'ActiveConnectionConflict']
            .indexOf(expected);
}

function failureMessage(failure, fallback) {
    const labels = {
        Credential: 'The API key or secret-file credential could not be read.',
        Timeout: 'qBittorrent did not respond before the request deadline.',
        Connection: 'The qBittorrent address could not be reached.',
        Authentication: 'qBittorrent rejected the configured authentication.',
        InvalidResponse: 'qBittorrent returned an unexpected response.',
        UnsupportedVersion: 'This qBittorrent application or Web API version is not supported.',
        JournalPersistence: 'QControl could not persist its recovery journal.'
    };
    return labels[String(failure ?? '')] ?? fallback;
}

function statusAnnouncement(status) {
    const lifecycle = protectionState(property(status, 'ProtectionState', 'Inactive'));
    const connectivity = connectivityState(property(status, 'Connectivity', 'Unconfigured'));
    const error = property(status, 'CurrentError', null);
    return `${lifecycle}. qBittorrent ${connectivity}.${error === null ? '' : ` Error ${String(error)}.`}`;
}

/**
 * Binds the QControl administrator page to server-owned contracts.
 *
 * @param {HTMLElement} view Jellyfin page root
 * @returns {void}
 */
export default function createPageController(view) {
    const apiClient = window.ApiClient;
    const dashboard = window.Dashboard;
    const state = {
        configuration: null,
        discoveredCategories: [],
        discoveredTags: [],
        exclusionTags: [],
        isTagCatalogAvailable: false,
        categoryEditorInitialized: false,
        active: false,
        statusTimer: null,
        pendingRecovery: null,
        recoveryOpener: null
    };
    const query = selector => view.querySelector(selector);
    const queryAll = selector => [...view.querySelectorAll(selector)];
    const announce = createAnnouncementGate(message => {
        query('#qControlPageAnnouncement').textContent = message;
    });

    function setInlineStatus(selector, message, kind = '') {
        const element = query(selector);
        element.textContent = message ?? '';
        element.classList.remove('qControlError', 'qControlWarning', 'qControlSuccess');
        if (kind) {
            element.classList.add(`qControl${kind}`);
        }
    }

    function setHidden(selector, shouldHide) {
        const element = query(selector);
        element.hidden = shouldHide;
        if (shouldHide) {
            element.setAttribute('hidden', '');
        } else {
            element.removeAttribute('hidden');
        }
    }

    function readEditor() {
        return {
            revision: Number(property(state.configuration, 'Revision', 0)),
            baseAddress: query('#qControlBaseAddress').value,
            credentialMode: query('#qControlCredentialMode').value,
            apiKeyReplacement: query('#qControlApiKey').value,
            clearStoredApiKey: query('#qControlClearStoredKey').checked,
            secretFilePath: query('#qControlSecretFilePath').value,
            alternativeLimitsEnabled: query('#qControlAlternativeLimitsEnabled').checked,
            stopTorrentsEnabled: query('#qControlStopTorrentsEnabled').checked,
            stopScope: query('#qControlStopScope').value,
            selectedCategories: queryAll('[data-category-choice]:checked').map(element => element.value),
            includeIncomplete: query('#qControlIncludeIncomplete').checked,
            includeCompleted: query('#qControlIncludeCompleted').checked,
            markerTag: query('#qControlMarkerTag').value,
            exclusionTags: [...state.exclusionTags],
            releaseGraceSeconds: query('#qControlReleaseGraceSeconds').value
        };
    }

    function renderCategories(preserveEditorSelection = true) {
        const configured = property(state.configuration, 'SelectedCategories', []) ?? [];
        const selectedValues = preserveEditorSelection && state.categoryEditorInitialized
            ? readEditor().selectedCategories
            : configured.map(value => String(value));
        const selected = new Set(selectedValues);
        const choices = mergeCategoryChoices(state.discoveredCategories, configured);
        const container = query('#qControlCategoryChoices');
        if (choices.length === 0) {
            container.innerHTML = '<p class="fieldDescription">No categories were returned. Uncategorized torrents are included only by All scope.</p>';
            state.categoryEditorInitialized = true;
            return;
        }

        container.innerHTML = choices.map((choice, index) => `
            <div class="checkboxContainer qControlCategoryChoice">
                <label class="emby-checkbox-label" for="qControlCategory${index}">
                    <input is="emby-checkbox" id="qControlCategory${index}" type="checkbox"
                           data-category-choice value="${escapeHtml(choice.value)}"${selected.has(choice.value) ? ' checked' : ''} />
                    <span>${escapeHtml(choice.value || '(uncategorized)')}</span>
                </label>
                ${choice.missing ? '<div class="qControlMissing">Configured, but not currently returned by qBittorrent.</div>' : ''}
            </div>`).join('');
        state.categoryEditorInitialized = true;
    }

    function renderExclusionTags() {
        const list = query('#qControlExclusionTagList');
        list.innerHTML = state.exclusionTags.length === 0
            ? '<span class="fieldDescription">No Exclusion Tags configured.</span>'
            : state.exclusionTags.map(tag => `
                <div class="qControlTagItem" role="listitem">
                    <span>${escapeHtml(tag)}</span>
                    <button is="emby-button" type="button" class="button"
                            data-action="remove-exclusion-tag" data-tag="${escapeHtml(tag)}"
                            aria-label="Remove exclusion tag ${escapeHtml(tag)}">Remove</button>
                </div>`).join('');
        query('#qControlExclusionTagSuggestions').innerHTML = mergeTagSuggestions(
            state.discoveredTags,
            state.exclusionTags).map(tag => `<option value="${escapeHtml(tag)}"></option>`).join('');
    }

    function addExclusionTag() {
        const input = query('#qControlExclusionTagInput');
        const tag = String(input.value ?? '').trim();
        const candidate = normalizedExclusionTags([...state.exclusionTags, tag]);
        const problem = exclusionTagProblem(candidate);
        if (!tag || problem) {
            setInlineStatus(
                '#qControlClientValidation',
                !tag ? 'Enter an Exclusion Tag before selecting Add.' : problem,
                'Warning');
            return;
        }

        state.exclusionTags = candidate;
        input.value = '';
        renderExclusionTags();
        updateEditorState();
    }

    function removeExclusionTag(tag) {
        state.exclusionTags = state.exclusionTags.filter(value => value !== tag);
        renderExclusionTags();
        updateEditorState();
    }

    function updateEditorState() {
        const credentialMode = enumValue(
            query('#qControlCredentialMode').value,
            ['StoredApiKey', 'SecretFile', 'Unauthenticated'],
            0);
        setHidden('#qControlStoredCredential', credentialMode !== 0);
        setHidden('#qControlSecretCredential', credentialMode !== 1);
        const selectedScope = enumValue(query('#qControlStopScope').value, ['All', 'SelectedCategories'], 0) === 1;
        setHidden('#qControlCategorySelection', !selectedScope);
        const problem = validateEditor(readEditor());
        setInlineStatus('#qControlClientValidation', problem ?? '', problem ? 'Warning' : '');
        query('[data-action="save-configuration"]').disabled = Boolean(problem);
        query('[data-action="test-connection"]').disabled = Boolean(problem);
        query('#qControlStopTorrentsEnabled').setAttribute('aria-invalid', problem ? 'true' : 'false');
    }

    function renderConnection(configuration) {
        query('#qControlBaseAddress').value = String(property(configuration, 'QbittorrentBaseAddress', '') ?? '');
        query('#qControlCredentialMode').value = String(enumValue(
            property(configuration, 'CredentialMode', 0),
            ['StoredApiKey', 'SecretFile', 'Unauthenticated'],
            0));
        const apiKeyInput = query('#qControlApiKey');
        apiKeyInput.value = '';
        const hasStoredKey = Boolean(property(configuration, 'HasStoredApiKey', false));
        apiKeyInput.placeholder = hasStoredKey
            ? 'Configured — paste to replace'
            : 'Paste a qBittorrent API key';
        query('#qControlApiKeyHelp').textContent = hasStoredKey
            ? 'A key is stored. Its content was not returned; paste a replacement and select Set API key to update it.'
            : 'Paste a qBittorrent API key, then select Set API key. Existing key content is never returned to this page.';
        setHidden('#qControlClearStoredKeyContainer', !hasStoredKey);
        query('#qControlClearStoredKey').checked = false;
        query('#qControlSecretFilePath').value = String(property(configuration, 'SecretFilePath', '') ?? '');
    }

    function renderConfiguration(configuration) {
        state.configuration = configuration;
        state.categoryEditorInitialized = false;
        renderConnection(configuration);
        query('#qControlAlternativeLimitsEnabled').checked = Boolean(property(configuration, 'AlternativeLimitsEnabled', false));
        query('#qControlStopTorrentsEnabled').checked = Boolean(property(configuration, 'StopTorrentsEnabled', false));
        query('#qControlStopScope').value = String(enumValue(
            property(configuration, 'StopScope', 0),
            ['All', 'SelectedCategories'],
            0));
        query('#qControlIncludeIncomplete').checked = Boolean(property(configuration, 'IncludeIncomplete', true));
        query('#qControlIncludeCompleted').checked = Boolean(property(configuration, 'IncludeCompleted', true));
        query('#qControlMarkerTag').value = String(property(configuration, 'MarkerTag', 'qcontrol-resume') ?? '');
        state.exclusionTags = normalizedExclusionTags(property(configuration, 'ExclusionTags', ['qcontrol-ignore']) ?? []);
        renderExclusionTags();
        query('#qControlReleaseGraceSeconds').value = String(property(configuration, 'ReleaseGraceSeconds', 60));
        renderCategories(false);
        updateEditorState();
    }

    function renderSavedConnection(configuration) {
        state.configuration = configuration;
        renderConnection(configuration);
        updateEditorState();
    }

    function renderOperationalStatus(status) {
        const summary = formatOperationalStatus(status);
        query('#qControlStatusSummary').innerHTML = `
            <h3>${escapeHtml(summary.headline)}</h3>
            <ul>${summary.details.map(detail => `<li>${escapeHtml(detail)}</li>`).join('')}</ul>`;
        query('[data-action="resume-marked"]').disabled = !Boolean(property(status, 'CanResumeMarkedTorrents', false));
        query('[data-action="restore-speed"]').disabled = !Boolean(property(status, 'CanRestorePreviousSpeedSetting', false));
        query('[data-action="mark-resolved"]').disabled = !Boolean(property(status, 'CanMarkResolved', false));
        announce(statusAnnouncement(status));
    }

    function scheduleStatusRefresh() {
        if (!state.active) {
            return;
        }

        if (state.statusTimer !== null) {
            window.clearTimeout(state.statusTimer);
        }

        state.statusTimer = window.setTimeout(async () => {
            state.statusTimer = null;
            await refreshStatus();
        }, statusRefreshMilliseconds);
    }

    async function refreshStatus() {
        try {
            const status = await requestJson(apiClient, 'GET', 'QControl/Status');
            renderOperationalStatus(status);
        } catch {
            query('#qControlStatusSummary').innerHTML = '<p class="qControlWarning">Operational status is temporarily unavailable.</p>';
            announce('QControl operational status is unavailable.');
        } finally {
            scheduleStatusRefresh();
        }
    }

    async function discoverSavedCategories() {
        try {
            const result = await requestJson(apiClient, 'GET', 'QControl/Connection/Categories');
            if (Boolean(property(result, 'IsConnected', false))) {
                state.discoveredCategories = property(result, 'Categories', []) ?? [];
                state.discoveredTags = property(result, 'Tags', []) ?? [];
                state.isTagCatalogAvailable = Boolean(property(result, 'IsTagCatalogAvailable', false));
                renderCategories();
                renderExclusionTags();
                setInlineStatus(
                    '#qControlTagCatalogStatus',
                    state.isTagCatalogAvailable ? '' : 'Tag suggestions are temporarily unavailable.',
                    state.isTagCatalogAvailable ? '' : 'Warning');
            }
        } catch {
            // The connection panel reports explicit tests; loading remains usable offline.
        }
    }

    async function testConnection() {
        const editor = readEditor();
        const problem = validateEditor(editor);
        if (problem) {
            setInlineStatus('#qControlConnectionStatus', problem, 'Warning');
            return;
        }

        setInlineStatus('#qControlConnectionStatus', 'Testing the read-only qBittorrent connection…');
        try {
            const result = await requestJson(
                apiClient,
                'POST',
                'QControl/Connection/Test',
                buildConfigurationCandidate(editor));
            if (!Boolean(property(result, 'IsConnected', false))) {
                setInlineStatus(
                    '#qControlConnectionStatus',
                    failureMessage(
                        property(result, 'Failure', null),
                        'The server could not validate this qBittorrent connection.'),
                    'Error');
                return;
            }

            state.discoveredCategories = property(result, 'Categories', []) ?? [];
            state.discoveredTags = property(result, 'Tags', []) ?? [];
            state.isTagCatalogAvailable = Boolean(property(result, 'IsTagCatalogAvailable', false));
            renderCategories();
            renderExclusionTags();
            setInlineStatus(
                '#qControlTagCatalogStatus',
                state.isTagCatalogAvailable ? '' : 'Connected, but tag suggestions are temporarily unavailable.',
                state.isTagCatalogAvailable ? '' : 'Warning');
            setInlineStatus(
                '#qControlConnectionStatus',
                `Connected to qBittorrent ${property(result, 'ApplicationVersion', 'unknown')} (Web API ${property(result, 'WebApiVersion', 'unknown')}).`,
                'Success');
        } catch (error) {
            const result = responsePayload(error);
            setInlineStatus(
                '#qControlConnectionStatus',
                failureMessage(
                    property(result, 'Failure', null),
                    'The server could not test this qBittorrent connection.'),
                'Error');
        }
    }

    async function setCredential(kind) {
        const editor = readEditor();
        if (kind === 'api-key') {
            if (!String(editor.apiKeyReplacement ?? '').trim()) {
                setInlineStatus('#qControlConnectionStatus', 'Paste an API key before selecting Set API key.', 'Warning');
                return;
            }

            editor.credentialMode = 0;
            editor.clearStoredApiKey = false;
        } else {
            if (!String(editor.secretFilePath ?? '').trim()) {
                setInlineStatus('#qControlConnectionStatus', 'Enter a file path before selecting Set file path.', 'Warning');
                return;
            }

            editor.credentialMode = 1;
            editor.apiKeyReplacement = '';
            editor.clearStoredApiKey = false;
        }

        const label = kind === 'api-key' ? 'API key' : 'API-key file path';
        setInlineStatus('#qControlConnectionStatus', `Setting the ${label}…`);
        try {
            const result = await requestJson(
                apiClient,
                'PUT',
                'QControl/Configuration',
                buildConnectionCandidate(state.configuration, editor));
            if (!outcomeEquals(property(result, 'Outcome', null), 'Accepted')) {
                setInlineStatus('#qControlConnectionStatus', saveOutcomeMessage(result), 'Error');
                return;
            }

            renderSavedConnection(property(result, 'Configuration', {}));
            setInlineStatus('#qControlConnectionStatus', `${label} set and saved.`, 'Success');
            await refreshStatus();
        } catch (error) {
            const result = responsePayload(error);
            setInlineStatus(
                '#qControlConnectionStatus',
                result ? saveOutcomeMessage(result) : `The server could not set the ${label}.`,
                'Error');
        }
    }

    function saveOutcomeMessage(result) {
        const outcome = property(result, 'Outcome', 'Invalid');
        if (outcomeEquals(outcome, 'RevisionConflict')) {
            return 'This page edited an older configuration revision. Reload the page before saving.';
        }

        if (outcomeEquals(outcome, 'ActiveConnectionConflict')) {
            return 'The qBittorrent address or authentication mode cannot change during an active protection session.';
        }

        if (outcomeEquals(outcome, 'ConnectionFailed')) {
            return failureMessage(
                property(result, 'Failure', null),
                'The enabled configuration did not pass the server connection test.');
        }

        return 'The server rejected this configuration. Review the complete connection, action, scope, lifecycle, tag, and grace settings.';
    }

    async function saveConfiguration() {
        const editor = readEditor();
        const problem = validateEditor(editor);
        if (problem) {
            setInlineStatus('#qControlConfigurationStatus', problem, 'Warning');
            return;
        }

        setInlineStatus('#qControlConfigurationStatus', 'Validating and saving the complete configuration…');
        try {
            const result = await requestJson(
                apiClient,
                'PUT',
                'QControl/Configuration',
                buildConfigurationCandidate(editor));
            if (!outcomeEquals(property(result, 'Outcome', null), 'Accepted')) {
                setInlineStatus('#qControlConfigurationStatus', saveOutcomeMessage(result), 'Error');
                return;
            }

            renderConfiguration(property(result, 'Configuration', {}));
            setInlineStatus(
                '#qControlConfigurationStatus',
                'Configuration saved. Behavior changes apply to the next activation when protection is already active.',
                'Success');
            await refreshStatus();
        } catch (error) {
            const result = responsePayload(error);
            setInlineStatus(
                '#qControlConfigurationStatus',
                result ? saveOutcomeMessage(result) : 'The server could not save this configuration.',
                'Error');
        }
    }

    function openRecovery(action, opener) {
        const command = recoveryCommand(action);
        state.pendingRecovery = command;
        state.recoveryOpener = opener;
        query('#qControlRecoveryDialogTitle').textContent = command.title;
        query('#qControlRecoveryDialogDescription').textContent = command.description;
        const dialog = query('#qControlRecoveryDialog');
        if (!dialog.open) {
            dialog.showModal();
        }

        query('[data-action="confirm-recovery"]').focus();
    }

    function closeRecovery() {
        const opener = state.recoveryOpener;
        const dialog = query('#qControlRecoveryDialog');
        if (dialog.open) {
            dialog.close();
        }

        state.pendingRecovery = null;
        state.recoveryOpener = null;
        opener?.focus();
    }

    async function confirmRecovery() {
        const command = state.pendingRecovery;
        if (!command) {
            return;
        }

        const button = query('[data-action="confirm-recovery"]');
        button.disabled = true;
        setInlineStatus('#qControlRecoveryStatus', 'Running the confirmed recovery action…');
        try {
            const result = await requestJson(apiClient, 'POST', command.endpoint);
            const outcome = String(property(result, 'Outcome', 'Failed'));
            if (outcome.toLowerCase() === 'completed' || Number(outcome) === 0) {
                setInlineStatus('#qControlRecoveryStatus', 'Recovery action completed.', 'Success');
                closeRecovery();
                await refreshStatus();
                return;
            }

            setInlineStatus(
                '#qControlRecoveryStatus',
                failureMessage(property(result, 'Failure', null), 'This recovery action is not currently available.'),
                'Warning');
        } catch (error) {
            const result = responsePayload(error);
            setInlineStatus(
                '#qControlRecoveryStatus',
                failureMessage(property(result, 'Failure', null), 'The recovery action did not complete.'),
                'Error');
        } finally {
            button.disabled = false;
        }
    }

    async function load() {
        dashboard.showLoadingMsg();
        try {
            const [configuration, status] = await Promise.all([
                requestJson(apiClient, 'GET', 'QControl/Configuration'),
                requestJson(apiClient, 'GET', 'QControl/Status')
            ]);
            renderConfiguration(configuration);
            renderOperationalStatus(status);
            const form = query('#qControlConfigurationForm');
            form.removeAttribute('inert');
            form.setAttribute('aria-busy', 'false');
            await discoverSavedCategories();
        } catch {
            setInlineStatus(
                '#qControlConfigurationStatus',
                'The administrator page could not load its server data.',
                'Error');
        } finally {
            dashboard.hideLoadingMsg();
            scheduleStatusRefresh();
        }
    }

    view.addEventListener('click', async event => {
        const button = event.target.closest('button[data-action]');
        if (!button || !view.contains(button) || button.disabled) {
            return;
        }

        switch (button.dataset.action) {
            case 'refresh-status':
                await refreshStatus();
                break;
            case 'test-connection':
                await testConnection();
                break;
            case 'set-api-key':
                await setCredential('api-key');
                break;
            case 'set-secret-file':
                await setCredential('secret-file');
                break;
            case 'save-configuration':
                await saveConfiguration();
                break;
            case 'add-exclusion-tag':
                addExclusionTag();
                break;
            case 'remove-exclusion-tag':
                removeExclusionTag(String(button.dataset.tag ?? ''));
                break;
            case 'resume-marked':
            case 'restore-speed':
            case 'mark-resolved':
                openRecovery(button.dataset.action, button);
                break;
            case 'confirm-recovery':
                await confirmRecovery();
                break;
            case 'cancel-recovery':
                closeRecovery();
                break;
        }
    });

    // Jellyfin's customized form controls may consume bubbling events while updating their wrappers.
    view.addEventListener('change', updateEditorState, true);
    view.addEventListener('input', updateEditorState, true);
    query('#qControlExclusionTagInput').addEventListener('keydown', event => {
        if (event.key === 'Enter') {
            event.preventDefault();
            addExclusionTag();
        }
    });
    query('#qControlRecoveryDialog').addEventListener('cancel', event => {
        event.preventDefault();
        closeRecovery();
    });
    view.addEventListener('viewshow', async () => {
        state.active = true;
        await load();
    });
    view.addEventListener('viewhide', () => {
        state.active = false;
        if (state.statusTimer !== null) {
            window.clearTimeout(state.statusTimer);
            state.statusTimer = null;
        }
    });
}
