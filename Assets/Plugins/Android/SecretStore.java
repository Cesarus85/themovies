package com.themovies.player;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import android.util.Log;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * Encrypts small secrets (NAS passwords, media server tokens) with an AES-256-GCM key that lives in the
 * Android Keystore: the key never leaves the system's key storage, so the saved text is useless on another
 * device or after the app was reinstalled. Output: Base64 of [IV length][IV][ciphertext + tag].
 */
public final class SecretStore {
    private static final String TAG = "TheMovies";
    private static final String KEYSTORE = "AndroidKeyStore";
    private static final String ALIAS = "themovies-secrets";
    private static final String TRANSFORMATION = "AES/GCM/NoPadding";

    private SecretStore() { }

    private static synchronized SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance(KEYSTORE);
        store.load(null);
        KeyStore.Entry entry = store.getEntry(ALIAS, null);
        if (entry instanceof KeyStore.SecretKeyEntry) return ((KeyStore.SecretKeyEntry) entry).getSecretKey();
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE);
        generator.init(new KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return generator.generateKey();
    }

    /** Encrypted text, or null if the keystore is not usable. */
    public static String encrypt(String plain) {
        try {
            Cipher cipher = Cipher.getInstance(TRANSFORMATION);
            cipher.init(Cipher.ENCRYPT_MODE, key());
            byte[] iv = cipher.getIV();
            byte[] data = cipher.doFinal(plain.getBytes(StandardCharsets.UTF_8));
            byte[] out = new byte[1 + iv.length + data.length];
            out[0] = (byte) iv.length;
            System.arraycopy(iv, 0, out, 1, iv.length);
            System.arraycopy(data, 0, out, 1 + iv.length, data.length);
            return Base64.encodeToString(out, Base64.NO_WRAP);
        } catch (Exception e) {
            Log.e(TAG, "SecretStore.encrypt failed", e);
            return null;
        }
    }

    /** The original text, or null if it can't be decrypted (other device, key deleted, damaged). */
    public static String decrypt(String encrypted) {
        try {
            byte[] in = Base64.decode(encrypted, Base64.NO_WRAP);
            int ivLength = in[0] & 0xFF;
            if (ivLength < 12 || ivLength > 32 || in.length < 1 + ivLength + 16) return null;
            Cipher cipher = Cipher.getInstance(TRANSFORMATION);
            cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, in, 1, ivLength));
            byte[] plain = cipher.doFinal(in, 1 + ivLength, in.length - 1 - ivLength);
            return new String(plain, StandardCharsets.UTF_8);
        } catch (Exception e) {
            Log.e(TAG, "SecretStore.decrypt failed", e);
            return null;
        }
    }
}
