package com.swrve.unity.gcm;

import android.app.Notification;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.support.v4.app.NotificationCompat;
import android.util.Log;
import java.util.Date;

import com.google.android.gms.gcm.GcmListenerService;
import com.swrve.sdk.SwrvePushSDK;
import com.swrve.unity.SwrveNotification;
import com.swrve.unity.SwrvePushSupport;
import com.unity3d.player.UnityPlayer;

public class SwrveGcmIntentService extends GcmListenerService {
	private static final String LOG_TAG = "SwrveGcmIntentService";

	@Override
	public void onMessageReceived(String from, Bundle data) {
		processRemoteNotification(data);
	}

	private void processRemoteNotification(Bundle msg) {
		try {
			if (SwrvePushSDK.isSwrveRemoteNotification(msg)) {
				final SharedPreferences prefs = SwrveGcmDeviceRegistration.getGCMPreferences(getApplicationContext());
				String activityClassName = SwrvePushSupport.getActivityClassName(getApplicationContext(), prefs);
				
				// Only call this listener if there is an activity running
				if (UnityPlayer.currentActivity != null) {
					// Call Unity SDK MonoBehaviour container
					SwrveNotification swrveNotification = SwrveNotification.Builder.build(msg);
					SwrveGcmDeviceRegistration.newReceivedNotification(SwrveGcmDeviceRegistration.getGameObject(UnityPlayer.currentActivity), SwrveGcmDeviceRegistration.ON_NOTIFICATION_RECEIVED_METHOD, swrveNotification);
		    	}
		
				// Process notification
				processNotification(msg, activityClassName);
			}
		} catch (Exception ex) {
			Log.e(LOG_TAG, "Error processing push notification", ex);
		}
	}

	/**
	 * Override this function to process notifications in a different way.
	 * 
	 * @param msg
	 * @param activityClassName
	 * 			game activity
	 */
	public void processNotification(final Bundle msg, String activityClassName) {
		// Put the message into a notification and post it.
		final NotificationManager mNotificationManager = (NotificationManager) this.getSystemService(Context.NOTIFICATION_SERVICE);
		final PendingIntent contentIntent = createPendingIntent(msg, activityClassName);
		final Notification notification = createNotification(msg, contentIntent);
		if (notification != null) {
			showNotification(mNotificationManager, notification);
		}
	}

	/**
	 * Override this function to change the way a notification is shown.
	 * 
	 * @param notificationManager
	 * @param notification
	 * @return the notification id so that it can be dismissed by other UI
	 *         elements
	 */
	public int showNotification(NotificationManager notificationManager, Notification notification) {
		int notificationId = generateNotificationId(notification);
		notificationManager.notify(notificationId, notification);
		return notificationId;
	}

	/**
	 * Generate the id for the new notification.
	 *
	 * Defaults to the current milliseconds to have unique notifications.
	 * 
	 * @param notification notification data
	 * @return id for the notification to be displayed
	 */
	public int generateNotificationId(Notification notification) {
		return (int)(new Date().getTime() % Integer.MAX_VALUE);
	}

	/**
	 * Override this function to change the attributes of a notification.
	 * 
	 * @param msgText
	 * @param msg
	 * @return
	 */
	public NotificationCompat.Builder createNotificationBuilder(String msgText, Bundle msg) {
		Context context = getApplicationContext();
		SharedPreferences prefs = SwrveGcmDeviceRegistration.getGCMPreferences(context);
		return SwrvePushSupport.createNotificationBuilder(context, prefs, msgText, msg);
	}

	private static boolean isEmptyString(String str) {
		return (str == null || str.equals(""));
	}

	/**
	 * Override this function to change the way the notifications are created.
	 * 
	 * @param msg
	 * @param contentIntent
	 * @return
	 */
	public Notification createNotification(Bundle msg, PendingIntent contentIntent) {
		String msgText = msg.getString("text");

		if (!isEmptyString(msgText)) { // Build notification
			NotificationCompat.Builder mBuilder = createNotificationBuilder(msgText, msg);
			mBuilder.setContentIntent(contentIntent);
			return mBuilder.build();
		}

		return null;
	}

	/**
	 * Override this function to change what the notification will do once
	 * clicked by the user.
	 * 
	 * Note: sending the Bundle in an extra parameter "notification" is
	 * essential so that the Swrve SDK can be notified that the app was opened
	 * from the notification.
	 * 
	 * @param msg
	 * @param activityClassName
	 * 			game activity
	 * @return
	 */
	public PendingIntent createPendingIntent(Bundle msg, String activityClassName) {
		// Add notification to bundle
		Intent intent = createIntent(msg, activityClassName);
		return PendingIntent.getActivity(this, generatePendingIntentId(msg), intent, PendingIntent.FLAG_UPDATE_CURRENT);
	}

	/**
	 * Generate the id for the pending intent associated with
	 * the given push payload.
	 *
	 * Defaults to the current milliseconds to have unique notifications.
	 * 
	 * @param msg push message payload
	 * @return id for the notification to be displayed
	 */
	public int generatePendingIntentId(Bundle msg) {
		return (int)(new Date().getTime() % Integer.MAX_VALUE);
	}

	/**
	 * Override this function to change what the notification will do once
	 * clicked by the user.
	 * 
	 * Note: sending the Bundle in an extra parameter "notification" is
	 * essential so that the Swrve SDK can be notified that the app was opened
	 * from the notification.
	 * 
	 * @param msg
	 * @param activityClassName
	 * 			game activity
	 * @return
	 */
	public Intent createIntent(Bundle msg, String activityClassName) {
		return SwrvePushSupport.createIntent(this, msg, activityClassName);
	}
	
	/**
	 * Process the push notification received from GCM
	 * that opened the app.
	 * 
	 * @param context
	 * @param intent
	 * 			The intent that opened the activity
	 */
	public static void processIntent(Context context, Intent intent) {
		if (intent != null) {
			try {
				Bundle extras = intent.getExtras();
				if (extras != null && !extras.isEmpty()) {
					Bundle msg = extras.getBundle(SwrvePushSupport.NOTIFICATION_PAYLOAD_KEY);
					if (msg != null) {
						SwrveNotification notification = SwrveNotification.Builder.build(msg);
						SwrveGcmDeviceRegistration.newOpenedNotification(SwrveGcmDeviceRegistration.getGameObject(UnityPlayer.currentActivity), SwrveGcmDeviceRegistration.ON_OPENED_FROM_PUSH_NOTIFICATION_METHOD, notification);
					}
				}
			} catch(Exception ex) {
				Log.e(LOG_TAG, "Could not process push notification intent", ex);
			}
		}
	}
}
