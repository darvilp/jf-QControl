import { expect, test } from '@playwright/test';

const pluginId = 'ab18c878185648538f215028a1d5a7b2';

test.describe.configure({ mode: 'serial' });

async function signIn(page) {
    await page.goto('/web/');
    const username = page.getByRole('textbox', { name: /^user$/i });
    await expect(username).toBeVisible();
    await username.fill(process.env.QCONTROL_ADMIN_NAME);
    await page.getByRole('textbox', { name: /password/i }).fill(process.env.QCONTROL_ADMIN_PASSWORD);
    await page.getByRole('button', { name: /sign in/i }).click();
    await expect(page.getByRole('button', { name: process.env.QCONTROL_ADMIN_NAME })).toBeVisible();
}

async function openPluginConfiguration(page) {
    await page.goto(`/web/#/dashboard/plugins/${pluginId}?name=QControl`);
    const settings = page.locator('#addPluginPage').getByRole('link', { name: 'Settings', exact: true });
    await expect(settings).toBeVisible();
    await expect(settings).toHaveAttribute('href', '#/configurationpage?name=QControl');
    await settings.click();
    await expect(page.locator('#qControlPage')).toBeVisible();
}

async function authenticateApi(request, deviceId) {
    const response = await request.post('/Users/AuthenticateByName', {
        headers: {
            Authorization: `MediaBrowser Client="QControl Browser Contract", Device="Chromium", DeviceId="${deviceId}", Version="0.1.0.0"`
        },
        data: {
            Username: process.env.QCONTROL_ADMIN_NAME,
            Pw: process.env.QCONTROL_ADMIN_PASSWORD
        }
    });
    expect(response.ok()).toBeTruthy();
    return (await response.json()).AccessToken;
}

async function readStatus(request, token) {
    const response = await request.get('/QControl/Status', {
        headers: { 'X-Emby-Token': token }
    });
    expect(response.ok()).toBeTruthy();
    return response.json();
}

test('administrator configures the real page without credential round-trip', async ({ page }) => {
    const consoleText = [];
    page.on('console', message => consoleText.push(message.text()));
    await signIn(page);
    await openPluginConfiguration(page);

    await expect(page.getByRole('heading', { name: 'Current status' })).toBeVisible();
    await expect(page.locator('#qControlConfigurationForm')).toHaveAttribute('aria-busy', 'false');
    await expect(page.locator('#qControlConfigurationForm')).not.toHaveAttribute('inert', '');
    await expect(page.locator('#qControlApiKey')).toHaveValue('');
    await expect(page.locator('#qControlApiKey')).not.toHaveAttribute('value', /qbt_/i);

    await page.locator('#qControlBaseAddress').fill('http://qbittorrent:18180');
    await page.locator('#qControlCredentialMode').selectOption('1');
    await expect(page.locator('#qControlStoredCredential')).toBeHidden();
    await expect(page.locator('#qControlSecretCredential')).toBeVisible();
    await page.locator('#qControlSecretFilePath').fill('/run/secrets/qbittorrent-api-key');
    const connectionRequestPromise = page.waitForRequest(request =>
        request.method() === 'POST' && request.url().includes('/QControl/Connection/Test'));
    await page.getByRole('button', { name: 'Test connection' }).click();
    const connectionRequest = await connectionRequestPromise;
    const connectionCandidate = connectionRequest.postDataJSON();
    expect(connectionCandidate.QbittorrentBaseAddress).toBe('http://qbittorrent:18180');
    expect(connectionCandidate.CredentialMode).toBe(1);
    expect(connectionCandidate.SecretFilePath).toBe('/run/secrets/qbittorrent-api-key');
    expect(connectionCandidate.ApiKeyReplacement).toBe('');
    await expect(page.locator('#qControlConnectionStatus')).toContainText(/connected to qBittorrent 5\.2\.3/i);
    await expect(page.locator('#qControlExclusionTagSuggestions option[value="fixture"]')).toHaveCount(1);
    await expect(page.locator('#qControlExclusionTagSuggestions option[value="qcontrol-ignore"]')).toHaveCount(1);

    await page.getByRole('button', { name: 'Set file path' }).click();
    await expect(page.locator('#qControlConnectionStatus')).toContainText(/file path set and saved/i);

    await page.getByText('Use qBittorrent Alternative Limits during playback', { exact: true }).click();
    await expect(page.locator('#qControlAlternativeLimitsEnabled')).toBeChecked();
    await page.getByText('Stop selected torrents during playback', { exact: true }).click();
    await expect(page.locator('#qControlStopTorrentsEnabled')).toBeChecked();
    await page.locator('#qControlStopScope').selectOption('1');
    await expect(page.locator('#qControlCategorySelection')).toBeVisible();
    await page.getByText('radarr', { exact: true }).click();
    await expect(page.getByLabel('radarr', { exact: true })).toBeChecked();
    await page.locator('#qControlExclusionTagInput').fill('browser-custom-ignore');
    await page.getByRole('button', { name: 'Add', exact: true }).click();
    await expect(page.locator('#qControlExclusionTagList')).toContainText('browser-custom-ignore');
    await page.locator('#qControlReleaseGraceSeconds').fill('1');
    const saveRequestPromise = page.waitForRequest(request =>
        request.method() === 'PUT' && request.url().includes('/QControl/Configuration'));
    await page.getByRole('button', { name: 'Save configuration' }).click();
    const saveCandidate = (await saveRequestPromise).postDataJSON();
    expect(saveCandidate.ExclusionTags).toEqual(['browser-custom-ignore', 'qcontrol-ignore']);
    await expect(page.locator('#qControlConfigurationStatus')).toContainText(/configuration saved/i);
    await expect(page.locator('#qControlApiKey')).toHaveValue('');
    expect(consoleText.join('\n')).not.toMatch(/qbt_/i);

    await page.locator('#qControlBaseAddress').focus();
    await page.keyboard.press('Tab');
    await expect(page.locator('#qControlCredentialMode')).toBeFocused();

    await page.setViewportSize({ width: 390, height: 844 });
    await expect(page.locator('#qControlConnection')).toBeVisible();
    await expect(page.locator('#qControlProtection')).toBeVisible();
    const overflow = await page.locator('#qControlPage').evaluate(element =>
        element.scrollWidth - element.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
});

test('manual recovery is inert until its native confirmation is accepted', async ({ page }) => {
    const recoveryRequests = [];
    page.on('request', request => {
        if (request.url().includes('/QControl/Recovery/')) {
            recoveryRequests.push(request.url());
        }
    });
    await signIn(page);
    await openPluginConfiguration(page);

    const opener = page.getByRole('button', { name: 'Mark resolved without changing qBittorrent' });
    await expect(opener).toBeEnabled();
    await opener.click();
    const dialog = page.locator('#qControlRecoveryDialog');
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText(/does not change qBittorrent/i);
    expect(recoveryRequests).toHaveLength(0);

    await page.getByRole('button', { name: 'Cancel', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(opener).toBeFocused();
    expect(recoveryRequests).toHaveLength(0);

    await opener.click();
    await page.getByRole('button', { name: 'Confirm recovery action' }).click();
    await expect(page.locator('#qControlRecoveryStatus')).toContainText(/completed/i);
    expect(recoveryRequests).toHaveLength(1);
});

test('real Jellyfin web player drives paused protection and normal release', async ({ page, request }) => {
    const apiToken = await authenticateApi(request, 'qcontrol-browser-status');
    const itemsResponse = await request.get('/Items?Recursive=true&IncludeItemTypes=Movie', {
        headers: { 'X-Emby-Token': apiToken }
    });
    expect(itemsResponse.ok()).toBeTruthy();
    const movie = (await itemsResponse.json()).Items[0];
    expect(movie).toBeTruthy();

    await signIn(page);
    await page.goto(`/web/#/details?id=${movie.Id}`);
    await expect(page.getByRole('heading', { name: movie.Name, exact: true })).toBeVisible();
    await page.getByRole('button', { name: /^play$/i }).first().click();

    const video = page.locator('video').first();
    await expect(video).toBeVisible();
    await expect.poll(() => video.evaluate(element => element.paused)).toBe(false);
    await video.evaluate(element => element.pause());
    await expect.poll(() => video.evaluate(element => element.paused)).toBe(true);

    await expect.poll(async () => {
        const status = await readStatus(request, apiToken);
        return {
            state: (status.ProtectionState ?? status.protectionState).toLowerCase(),
            sessions: status.QualifyingSessionCount ?? status.qualifyingSessionCount,
            limits: status.AlternativeLimitsCurrentlyEnabled
                ?? status.alternativeLimitsCurrentlyEnabled,
            marked: status.MarkedTorrentCount ?? status.markedTorrentCount
        };
    }, { timeout: 45_000 }).toEqual({
        state: 'protecting',
        sessions: 1,
        limits: true,
        marked: 1
    });

    await video.evaluate(async element => {
        await element.play();
    });
    await expect(page.getByRole('button', { name: /^play$/i }).first()).toBeVisible({
        timeout: 10_000
    });
    await expect.poll(async () => {
        const status = await readStatus(request, apiToken);
        return (status.ProtectionState ?? status.protectionState).toLowerCase();
    }, { timeout: 45_000 }).toBe('inactive');
});
