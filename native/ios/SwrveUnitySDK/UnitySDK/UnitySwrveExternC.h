#ifdef __cplusplus
extern "C"
{
#endif

    int _swrveiOSConversationVersion(void);
    char* _swrveiOSLanguage(void);
    char* _swrveiOSTimeZone(void);
    char* _swrveiOSAppVersion(void);
    char* _swrveiOSUUID(void);
    char* _swrveiOSCarrierName(void);
    char* _swrveiOSCarrierIsoCountryCode(void);
    char* _swrveiOSCarrierCode(void);
    char* _swrveiOSLocaleCountry(void);
    char* _swrveiOSIDFV(void);
    char* _swrveiOSIDFA(void);
    void _swrveiOSRegisterForPushNotifications(char* jsonUNCategorySet, bool provisional);
    void _swrveiOSInitNative(char* jsonConfig);
    void _swrveiOSShowConversation(char* conversation);
    bool _swrveiOSIsSupportedOSVersion(void);
    bool _swrveiOSIsConversationDisplaying(void);
    char* _swrveInfluencedDataJson(void);
    char* _swrvePushNotificationStatus(char* componentName);
    char* _swrveBackgroundRefreshStatus(void);
    void _swrveiOSUpdateQaUser(char* jsonMap);
    void _swrveUserId(char* userId);
    void _clearAllAuthenticatedNotifications(void);

#ifdef __cplusplus
}
#endif
