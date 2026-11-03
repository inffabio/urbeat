export enum PaymentMethod {
  /** Legacy online Pix. Kept only so in-flight Pix orders keep working; never offered again. */
  PixOnline = 1,
  /** Legacy online card. Kept only for historical/in-flight orders. */
  CardOnline = 2,
  CashOnDelivery = 3,
  /** Legacy generic card on delivery. Kept only for old orders. */
  CardOnDelivery = 4,
  /** Pix paid on the card machine at handover. */
  PixOnDelivery = 5,
  /** Debit card paid on the card machine at handover. */
  DebitOnDelivery = 6,
  /** Credit card paid on the card machine at handover. */
  CreditOnDelivery = 7,
}
